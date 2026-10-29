// Package signing implements the additional, explicitly dev/test-only Tunner
// plugin trust path. It does not replace Grafana's vendor signature verifier.
package signing

import (
	"bytes"
	"crypto/ed25519"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"io/fs"
	"strings"
)

const ManifestName = "TUNNER-DEV-MANIFEST.json"
const Scope = "tunner-lgtm-dev-test-v1"
const maxManifest = 16 << 20
const maxFiles = 20000

type Key struct {
	Public      ed25519.PublicKey
	Fingerprint string
	Revoked     bool
}
type Policy struct {
	Keys    map[string]Key
	Plugins map[string]string // exact plugin ID -> exact approved version
}
type Manifest struct {
	Schema      int               `json:"schema"`
	Scope       string            `json:"scope"`
	KeyID       string            `json:"key_id"`
	Fingerprint string            `json:"fingerprint"`
	Plugin      string            `json:"plugin"`
	Version     string            `json:"version"`
	Files       map[string]string `json:"files"`
}
type Envelope struct {
	Payload   string `json:"payload"`
	Signature string `json:"signature"`
}

// Verify returns handled=false ONLY when no Tunner manifest exists. Callers
// must then run the unchanged vendor verifier. A present invalid Tunner
// manifest always fails closed, including on nonallowlisted plugins.
func Verify(fsys fs.FS, plugin, version string, policy Policy) (handled bool, err error) {
	f, err := fsys.Open(ManifestName)
	if errors.Is(err, fs.ErrNotExist) {
		return false, nil
	}
	if err != nil {
		return true, fmt.Errorf("open Tunner manifest: %w", err)
	}
	raw, readErr := io.ReadAll(io.LimitReader(f, maxManifest+1))
	closeErr := f.Close()
	if readErr != nil || closeErr != nil || len(raw) > maxManifest {
		return true, errors.New("invalid Tunner manifest read/size")
	}
	if policy.Plugins[plugin] != version || version == "" {
		return true, errors.New("plugin/version is not allowlisted")
	}
	var env Envelope
	if err := strictJSON(raw, &env); err != nil {
		return true, err
	}
	payload, err := base64.StdEncoding.Strict().DecodeString(env.Payload)
	if err != nil {
		return true, errors.New("invalid manifest encoding")
	}
	var m Manifest
	if err := strictJSON(payload, &m); err != nil {
		return true, err
	}
	if m.Schema != 1 || m.Scope != Scope || m.Plugin != plugin || m.Version != version {
		return true, errors.New("manifest scope/identity mismatch")
	}
	key, ok := policy.Keys[m.KeyID]
	if !ok || key.Revoked || len(key.Public) != ed25519.PublicKeySize {
		return true, errors.New("unknown/revoked signing key")
	}
	fingerprint := sha256.Sum256(key.Public)
	expected := hex.EncodeToString(fingerprint[:])
	if key.Fingerprint != expected || m.Fingerprint != expected {
		return true, errors.New("public-key fingerprint mismatch")
	}
	signature, err := base64.StdEncoding.Strict().DecodeString(env.Signature)
	if err != nil || !ed25519.Verify(key.Public, payload, signature) {
		return true, errors.New("invalid Tunner signature")
	}
	if len(m.Files) == 0 || len(m.Files) > maxFiles {
		return true, errors.New("invalid file inventory size")
	}
	if _, ok := m.Files["MANIFEST.txt"]; !ok {
		return true, errors.New("original vendor manifest must be preserved")
	}
	if _, ok := m.Files["plugin.json"]; !ok {
		return true, errors.New("plugin metadata must be signed")
	}
	for name, hash := range m.Files {
		if !safePath(name) || name == ManifestName || len(hash) != 64 || strings.ToLower(hash) != hash {
			return true, errors.New("invalid manifest file entry")
		}
		if _, err := hex.DecodeString(hash); err != nil {
			return true, errors.New("invalid file hash")
		}
	}
	seen := 0
	err = fs.WalkDir(fsys, ".", func(name string, d fs.DirEntry, walkErr error) error {
		if walkErr != nil {
			return walkErr
		}
		if d.Type()&fs.ModeSymlink != 0 {
			return errors.New("symlink is prohibited")
		}
		if d.IsDir() {
			return nil
		}
		if !d.Type().IsRegular() || !safePath(name) {
			return errors.New("unsafe plugin file")
		}
		if name == ManifestName {
			return nil
		}
		expected, ok := m.Files[name]
		if !ok {
			return fmt.Errorf("unsigned extra file: %s", name)
		}
		file, err := fsys.Open(name)
		if err != nil {
			return err
		}
		info, err := file.Stat()
		if err != nil {
			file.Close()
			return err
		}
		if !info.Mode().IsRegular() {
			file.Close()
			return errors.New("nonregular plugin file")
		}
		h := sha256.New()
		_, copyErr := io.Copy(h, file)
		closeErr := file.Close()
		if copyErr != nil {
			return copyErr
		}
		if closeErr != nil {
			return closeErr
		}
		if hex.EncodeToString(h.Sum(nil)) != expected {
			return fmt.Errorf("plugin file modified: %s", name)
		}
		seen++
		return nil
	})
	if err != nil {
		return true, err
	}
	if seen != len(m.Files) {
		return true, errors.New("signed file is missing")
	}
	metadata, err := fs.ReadFile(fsys, "plugin.json")
	if err != nil {
		return true, err
	}
	var identity struct {
		ID   string `json:"id"`
		Info struct {
			Version string `json:"version"`
		} `json:"info"`
	}
	if err = json.Unmarshal(metadata, &identity); err != nil || identity.ID != plugin || identity.Info.Version != version {
		return true, errors.New("signed plugin metadata identity mismatch")
	}
	return true, nil
}

func safePath(name string) bool {
	return fs.ValidPath(name) && name != "." && !strings.ContainsAny(name, "\\:\x00\r\n")
}

// strictJSON rejects unknown fields, trailing values and duplicate keys at
// every nesting level, including duplicate file paths in the signed payload.
func strictJSON(raw []byte, out any) error {
	d := json.NewDecoder(bytes.NewReader(raw))
	if err := uniqueValue(d); err != nil {
		return err
	}
	if _, err := d.Token(); err != io.EOF {
		return errors.New("trailing JSON content")
	}
	d = json.NewDecoder(bytes.NewReader(raw))
	d.DisallowUnknownFields()
	return d.Decode(out)
}
func uniqueValue(d *json.Decoder) error {
	token, err := d.Token()
	if err != nil {
		return err
	}
	delim, ok := token.(json.Delim)
	if !ok {
		return nil
	}
	switch delim {
	case '{':
		seen := map[string]bool{}
		for d.More() {
			t, err := d.Token()
			if err != nil {
				return err
			}
			key, ok := t.(string)
			if !ok || seen[key] {
				return errors.New("duplicate or invalid JSON key")
			}
			seen[key] = true
			if err := uniqueValue(d); err != nil {
				return err
			}
		}
	case '[':
		for d.More() {
			if err := uniqueValue(d); err != nil {
				return err
			}
		}
	default:
		return errors.New("unexpected JSON delimiter")
	}
	_, err = d.Token()
	return err
}
