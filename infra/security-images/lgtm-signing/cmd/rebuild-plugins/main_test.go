package main

import (
	"bytes"
	"errors"
	"testing"
	"time"
)

func TestModuleDownloadRetriesAreBounded(t *testing.T) {
	for _, tc := range []struct {
		name     string
		failures int
		output   string
		calls    int
		failed   bool
	}{
		{"immediate success", 0, "", 1, false},
		{"transient recovery", 2, "net/http: TLS handshake timeout", 3, false},
		{"transient exhaustion", 5, "i/o timeout", 3, true},
		{"checksum failure", 5, "checksum mismatch; TLS handshake timeout", 1, true},
		{"unknown revision", 5, "unknown revision", 1, true},
		{"test assertion", 5, "FAIL expected true", 1, true},
	} {
		t.Run(tc.name, func(t *testing.T) {
			calls := 0
			waits := []time.Duration{}
			var log bytes.Buffer
			err := retryModuleDownload(func() ([]byte, error) {
				calls++
				if calls <= tc.failures {
					return []byte(tc.output), errors.New("download failed")
				}
				return []byte("resolved"), nil
			}, &log, func(d time.Duration) { waits = append(waits, d) })
			if calls != tc.calls || (err != nil) != tc.failed {
				t.Fatalf("calls=%d err=%v", calls, err)
			}
			if len(waits) != calls-1 {
				t.Fatalf("waits=%v calls=%d", waits, calls)
			}
			for i, d := range waits {
				if d != time.Duration((i+1)*2)*time.Second {
					t.Fatalf("unexpected delay %v", d)
				}
			}
		})
	}
}
func TestDownloadTransportClassifier(t *testing.T) {
	for _, message := range []string{"TLS handshake timeout", "i/o timeout", "connection reset by peer", "temporary failure in name resolution"} {
		if !transientDownloadFailure(message) {
			t.Fatalf("expected transient: %s", message)
		}
	}
	for _, message := range []string{"SECURITY ERROR: TLS handshake timeout", "verification failed; i/o timeout", "invalid version", "go: module does not contain package", "compile error", "HTTP 403"} {
		if transientDownloadFailure(message) {
			t.Fatalf("must remain blocking: %s", message)
		}
	}
}
