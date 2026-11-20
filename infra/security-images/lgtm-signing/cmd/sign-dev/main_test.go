package main

import (
	"crypto/ed25519"
	"encoding/base64"
	"encoding/json"
	"fmt"
 "go/parser"
 "go/token"
	"os"
	"path/filepath"
	"strings"
	"testing"
	signing "tunner.dev/lgtm-signing"
)

func signingFixture(t *testing.T) (string, string, string, string, inventory) {
	t.Helper()
	root := t.TempDir()
	plugins := filepath.Join(root, "plugins")
	if err := os.Mkdir(plugins, 0700); err != nil {
		t.Fatal(err)
	}
	inv := inventory{Scope: "DEV_TEST_ONLY_NOT_YET_ADOPTED"}
	for i := 0; i < 13; i++ {
		id := fmt.Sprintf("synthetic-plugin-%02d", i)
		p := pluginSpec{Directory: id, ID: id, Version: "1.0.0"}
		inv.Plugins = append(inv.Plugins, p)
		dir := filepath.Join(plugins, id)
		if err := os.Mkdir(dir, 0700); err != nil {
			t.Fatal(err)
		}
		for name, body := range map[string]string{"plugin.json": fmt.Sprintf(`{"id":%q,"info":{"version":"1.0.0"}}`, id), "MANIFEST.txt": "synthetic vendor provenance fixture", "binary_linux_amd64": "synthetic rebuilt content"} {
			if err := os.WriteFile(filepath.Join(dir, name), []byte(body), 0600); err != nil {
				t.Fatal(err)
			}
		}
	}
	source := filepath.Join(root, "inventory.json")
	raw, _ := json.Marshal(inv)
	if err := os.WriteFile(source, raw, 0600); err != nil {
		t.Fatal(err)
	}
	return plugins, source, filepath.Join(root, "policy.go"), filepath.Join(root, "receipt.json"), inv
}
func TestMemoryOnlySignerProducesVerifiableCompleteInventory(t *testing.T) {
	root, source, output, audit, inv := signingFixture(t)
	if err := signPlugins(root, source, output, audit); err != nil {
		t.Fatal(err)
	}
	receiptRaw, err := os.ReadFile(audit)
	if err != nil {
		t.Fatal(err)
	}
	var receipt struct {
		KeyID       string `json:"key_id"`
		Public      string `json:"public_key_base64"`
		Fingerprint string `json:"public_key_sha256"`
		Private     bool   `json:"private_material_exported"`
		Production  bool   `json:"production_trust"`
	}
	if err = json.Unmarshal(receiptRaw, &receipt); err != nil {
		t.Fatal(err)
	}
	if receipt.Private || receipt.Production {
		t.Fatal("must never establish production trust or export private material")
	}
	pub, err := base64.StdEncoding.DecodeString(receipt.Public)
	if err != nil || len(pub) != ed25519.PublicKeySize {
		t.Fatal("public key is required")
	}
	policy := signing.Policy{Keys: map[string]signing.Key{receipt.KeyID: {Public: ed25519.PublicKey(pub), Fingerprint: receipt.Fingerprint}}, Plugins: map[string]string{}}
	for _, p := range inv.Plugins {
		policy.Plugins[p.ID] = p.Version
	}
	for _, p := range inv.Plugins {
		handled, err := signing.Verify(os.DirFS(filepath.Join(root, p.Directory)), p.ID, p.Version, policy)
		if !handled || err != nil {
			t.Fatalf("plugin %s: %v", p.ID, err)
		}
	}
	if _,err:=parser.ParseFile(token.NewFileSet(),output,nil,parser.AllErrors);err!=nil{t.Fatalf("invalid generated public policy: %v",err)}
 generated, err := os.ReadFile(output)
	if err != nil {
		t.Fatal(err)
	}
	if !strings.Contains(string(generated), receipt.Fingerprint) || !strings.Contains(string(generated), "tunnerDevPolicy") {
		t.Fatal("generated policy must pin the actual public key")
	}
	// The only newly created entries are the thirteen public signed manifests,
	// generated public policy and public receipt. No private-key path exists.
	entries, err := os.ReadDir(filepath.Dir(root))
	if err != nil {
		t.Fatal(err)
	}
	if len(entries) != 4 {
		t.Fatalf("unexpected build output entry count: %d", len(entries))
	}
	for _, p := range inv.Plugins {
		entries, err := os.ReadDir(filepath.Join(root, p.Directory))
		if err != nil || len(entries) != 4 {
			t.Fatal("unexpected plugin/signing output")
		}
	}
	if err = os.WriteFile(filepath.Join(root, inv.Plugins[0].Directory, "binary_linux_amd64"), []byte("tamper"), 0600); err != nil {
		t.Fatal(err)
	}
	handled, err := signing.Verify(os.DirFS(filepath.Join(root, inv.Plugins[0].Directory)), inv.Plugins[0].ID, inv.Plugins[0].Version, policy)
	if !handled || err == nil {
		t.Fatal("signer output must reject subsequent tampering")
	}
}
func TestSignerRefusesExistingSignature(t *testing.T) {
	root, source, output, audit, inv := signingFixture(t)
	if err := os.WriteFile(filepath.Join(root, inv.Plugins[0].Directory, signing.ManifestName), []byte("existing"), 0600); err != nil {
		t.Fatal(err)
	}
	if signPlugins(root, source, output, audit) == nil {
		t.Fatal("must not overwrite signature")
	}
}
func TestSignerRejectsMissingVendorProvenance(t *testing.T) {
	root, source, output, audit, inv := signingFixture(t)
	if err := os.Remove(filepath.Join(root, inv.Plugins[0].Directory, "MANIFEST.txt")); err != nil {
		t.Fatal(err)
	}
	if signPlugins(root, source, output, audit) == nil {
		t.Fatal("must retain vendor provenance")
	}
}
func TestSignerRejectsNonDevScope(t *testing.T) {
	root, source, output, audit, inv := signingFixture(t)
	inv.Scope = "production"
	raw, _ := json.Marshal(inv)
	os.WriteFile(source, raw, 0600)
	if signPlugins(root, source, output, audit) == nil {
		t.Fatal("production scope is not approved")
	}
}
