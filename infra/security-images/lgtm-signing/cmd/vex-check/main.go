// vex-check validates the approved dev/test applicability scope. It never scans
// or filters an image itself; consumers retain the original unfiltered report.
package main

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"os"
	"path/filepath"
	"runtime"
	"sort"
	"strings"
	"time"
	signing "tunner.dev/lgtm-signing"
)

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
func hash(b []byte) string { s := sha256.Sum256(b); return hex.EncodeToString(s[:]) }
func load(dir, name string, out any) ([]byte, error) {
	raw, err := os.ReadFile(filepath.Join(dir, name))
	if err != nil {
		return nil, err
	}
	return raw, signing.DecodeVEXData(raw, out)
}
func run() error {
	approved := flag.String("approved-dir", "", "reviewed consent/policy/source/VEX/review directory")
	artifact := flag.String("binary", "", "extracted exact Tempo plugin executable")
	scanPath := flag.String("raw-scan", "", "original unfiltered scanner JSON")
	imageID := flag.String("image-id", "", "immutable inspected image identity")
	flag.Parse()
	if *approved == "" || *artifact == "" || *scanPath == "" || *imageID == "" {
		return errors.New("all four explicit evidence inputs are required")
	}
	var a signing.TempoVEXApproval
	ab, err := load(*approved, "approval.json", &a)
	if err != nil {
		return err
	}
	var p signing.TempoVEXBinding
	pb, err := load(*approved, "binding.json", &p)
	if err != nil {
		return err
	}
	if p.ApprovalSHA256 != hash(ab) {
		return errors.New("consent receipt changed after binding")
	}
	var source signing.TempoVEXSource
	sb, err := load(*approved, "source.json", &source)
	if err != nil {
		return err
	}
	var review signing.TempoVEXReviews
	_, err = load(*approved, "review.json", &review)
	if err != nil {
		return err
	}
	vex, err := os.ReadFile(filepath.Join(*approved, "tempo-vex.json"))
	if err != nil {
		return err
	}
	tests, err := os.ReadFile(filepath.Join(*approved, "tests.jsonl"))
	if err != nil {
		return err
	}
	codeHash, err := validatorSourceHash()
	if err != nil {
		return err
	}
	expected := map[string]string{"approval": hash(ab), "binding": hash(pb), "source": hash(sb), "vex": hash(vex), "tests": hash(tests)}
	if err = signing.ValidateTempoVEXReviews(review, expected, codeHash); err != nil {
		return err
	}
	if err = requiredTests(tests); err != nil {
		return err
	}
	raw, err := os.ReadFile(*scanPath)
	if err != nil {
		return err
	}
	binary, err := os.Open(*artifact)
	if err != nil {
		return err
	}
	defer binary.Close()
	count, err := signing.ValidateTempoVEX(a, p, source, binary, *imageID, raw, vex, time.Now().UTC())
	if err != nil {
		return err
	}
	if count == 0 {
		return errors.New("no matching raw finding; use the unfiltered scan")
	}
	return json.NewEncoder(os.Stdout).Encode(map[string]any{"result": "PASS_SCOPED_DEV_TEST_ONLY", "applicable_reviewed_findings": count, "image_id": *imageID, "validator_source_sha256": codeHash, "vex_sha256": hash(vex), "production_authorized": false})
}
func validatorSourceHash() (string, error) {
	_, file, _, ok := runtime.Caller(0)
	if !ok {
		return "", errors.New("cannot bind executing validator source")
	}
	root := filepath.Clean(filepath.Join(filepath.Dir(file), "../.."))
	paths := []string{"go.mod"}
	for _, pattern := range []string{"*.go", "cmd/vex-check/*.go"} {
		matches, err := filepath.Glob(filepath.Join(root, pattern))
		if err != nil {
			return "", err
		}
		for _, p := range matches {
			rel, err := filepath.Rel(root, p)
			if err != nil {
				return "", err
			}
			paths = append(paths, filepath.ToSlash(rel))
		}
	}
	sort.Strings(paths)
	var combined strings.Builder
	for _, p := range paths {
		body, err := os.ReadFile(filepath.Join(root, p))
		if err != nil {
			return "", err
		}
		body = []byte(strings.ReplaceAll(string(body), "\r\n", "\n"))
		combined.WriteString(p + "\x00" + hash(body) + "\n")
	}
	return hash([]byte(combined.String())), nil
}
func requiredTests(raw []byte) error {
	required := map[string]bool{"TestTempoVEXExactBinding": false, "TestTempoVEXRejections/wrong_source": false, "TestTempoVEXRejections/wrong_binary_hash": false, "TestTempoVEXRejections/wrong_CVE": false, "TestTempoVEXRejections/expired_consent": false, "TestTempoVEXRejections/wrong_target": false, "TestTempoVEXRejections/wrong_image": false, "TestTempoVEXRejectsDocumentTampering": false, "TestTempoVEXReviewRejections": false}
	decoder := json.NewDecoder(strings.NewReader(string(raw)))
	for {
		var event struct{ Action, Package, Test string }
		err := decoder.Decode(&event)
		if err == io.EOF {
			break
		}
		if err != nil {
			return err
		}
		if event.Action == "fail" {
			return errors.New("validator regression receipt contains a failure")
		}
		if event.Action == "pass" && event.Package == "tunner.dev/lgtm-signing" {
			if _, ok := required[event.Test]; ok {
				required[event.Test] = true
			}
		}
	}
	for test, passed := range required {
		if !passed {
			return fmt.Errorf("required VEX regression did not pass: %s", test)
		}
	}
	return nil
}
