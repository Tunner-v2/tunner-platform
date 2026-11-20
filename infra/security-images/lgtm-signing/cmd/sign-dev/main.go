// sign-dev creates a disposable synthetic signing identity wholly in memory.
// It emits public trust/provenance and signed plugin inventories, never a
// private key. Output is suitable only for the explicitly approved dev/test build.
package main

import (
	"crypto/ed25519"
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io"
	"io/fs"
	"os"
	"path/filepath"
	"strings"
	signing "tunner.dev/lgtm-signing"
)

type pluginSpec struct {
	Directory string `json:"directory"`
	ID        string `json:"id"`
	Version   string `json:"version"`
}
type inventory struct {
	Scope   string       `json:"scope"`
	Plugins []pluginSpec `json:"plugins"`
}

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
func run() error {
	root := flag.String("plugins", "", "complete rebuilt plugin directory")
	source := flag.String("inventory", "", "reviewed exact plugin inventory")
	output := flag.String("policy-output", "", "generated Grafana policy Go file")
	audit := flag.String("audit-output", "", "public-only signing provenance JSON")
	flag.Parse()
	return signPlugins(*root, *source, *output, *audit)
}
func signPlugins(root, source, output, audit string) error {
	if root == "" || source == "" || output == "" || audit == "" {
		return errors.New("all four explicit paths are required")
	}
	raw, err := os.ReadFile(source)
	if err != nil {
		return err
	}
	var inv inventory
	if err = json.Unmarshal(raw, &inv); err != nil {
		return err
	}
	if inv.Scope != "DEV_TEST_ONLY_NOT_YET_ADOPTED" || len(inv.Plugins) != 13 {
		return errors.New("unexpected dev/test signing inventory")
	}
	public, private, err := ed25519.GenerateKey(rand.Reader)
	if err != nil {
		return err
	}
	defer clear(private)
	sum := sha256.Sum256(public)
	fingerprint := hex.EncodeToString(sum[:])
	keyID := "synthetic-dev-" + fingerprint[:16]
	policy := signing.Policy{Keys: map[string]signing.Key{keyID: {Public: public, Fingerprint: fingerprint}}, Plugins: map[string]string{}}
	for _, p := range inv.Plugins {
		if p.ID == "" || p.Version == "" || !fs.ValidPath(p.Directory) || strings.Contains(p.Directory, "/") {
			return errors.New("invalid approved plugin identity")
		}
		if _, exists := policy.Plugins[p.ID]; exists {
			return errors.New("duplicate approved plugin identity")
		}
		policy.Plugins[p.ID] = p.Version
	}
	for _, p := range inv.Plugins {
		pluginRoot := filepath.Join(root, p.Directory)
		manifest := signing.Manifest{Schema: 1, Scope: signing.Scope, KeyID: keyID, Fingerprint: fingerprint, Plugin: p.ID, Version: p.Version, Files: map[string]string{}}
		err = filepath.WalkDir(pluginRoot, func(name string, d fs.DirEntry, walkErr error) error {
			if walkErr != nil {
				return walkErr
			}
			if d.Type()&fs.ModeSymlink != 0 {
				return errors.New("symlink in plugin build")
			}
			if d.IsDir() {
				return nil
			}
			if !d.Type().IsRegular() {
				return errors.New("nonregular plugin file")
			}
			rel, err := filepath.Rel(pluginRoot, name)
			if err != nil {
				return err
			}
			rel = filepath.ToSlash(rel)
			if rel == signing.ManifestName {
				return errors.New("refuse to overwrite an existing Tunner signature")
			}
			f, err := os.Open(name)
			if err != nil {
				return err
			}
			h := sha256.New()
			_, copyErr := io.Copy(h, f)
			closeErr := f.Close()
			if copyErr != nil {
				return copyErr
			}
			if closeErr != nil {
				return closeErr
			}
			manifest.Files[rel] = hex.EncodeToString(h.Sum(nil))
			return nil
		})
		if err != nil {
			return err
		}
		payload, err := json.Marshal(manifest)
		if err != nil {
			return err
		}
		env := signing.Envelope{Payload: base64.StdEncoding.EncodeToString(payload), Signature: base64.StdEncoding.EncodeToString(ed25519.Sign(private, payload))}
		signed, err := json.Marshal(env)
		if err != nil {
			return err
		}
		if err = os.WriteFile(filepath.Join(pluginRoot, signing.ManifestName), append(signed, '\n'), 0444); err != nil {
			return err
		}
		if handled, err := signing.Verify(os.DirFS(pluginRoot), p.ID, p.Version, policy); !handled || err != nil {
			return fmt.Errorf("signed plugin self-check %s: %w", p.ID, err)
		}
	}
	var generated strings.Builder
	generated.WriteString("// Generated only by the isolated synthetic dev/test signer. No private material.\npackage signature\n\nimport (\n \"crypto/ed25519\"\n tunnerdev \"github.com/grafana/grafana/pkg/plugins/manager/signature/tunnerdev\"\n)\n\nvar tunnerDevPolicy = tunnerdev.Policy{\n Keys: map[string]tunnerdev.Key{\n")
	fmt.Fprintf(&generated, "%q: {Public: ed25519.PublicKey{%s}, Fingerprint:%q},\n", keyID, byteList(public), fingerprint)
	generated.WriteString("}, Plugins: map[string]string{\n")
	for _, p := range inv.Plugins {
		fmt.Fprintf(&generated, "%q:%q,\n", p.ID, p.Version)
	}
	generated.WriteString("},\n}\n")
	if err = os.WriteFile(output, []byte(generated.String()), 0444); err != nil {
		return err
	}
	receipt := map[string]any{"scope": signing.Scope, "synthetic": true, "key_id": keyID, "public_key_base64": base64.StdEncoding.EncodeToString(public), "public_key_sha256": fingerprint, "plugins": inv.Plugins, "private_material_exported": false, "production_trust": false}
	proof, err := json.MarshalIndent(receipt, "", "  ")
	if err != nil {
		return err
	}
	return os.WriteFile(audit, append(proof, '\n'), 0444)
}
func byteList(b []byte) string {
	parts := make([]string, len(b))
	for i, v := range b {
		parts[i] = fmt.Sprint(v)
	}
	return strings.Join(parts, ",")
}
