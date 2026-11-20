package signing

import (
	"crypto/ed25519"
	"crypto/rand"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"io/fs"
	"strings"
	"testing"
	"testing/fstest"
)

func fixture(t *testing.T) (fstest.MapFS, Policy, Manifest, ed25519.PrivateKey) {
	t.Helper()
	pub, priv, err := ed25519.GenerateKey(rand.Reader)
	if err != nil {
		t.Fatal(err)
	}
	sum := sha256.Sum256(pub)
	fingerprint := hex.EncodeToString(sum[:])
	files := fstest.MapFS{
		"plugin.json":     {Data: []byte(`{"id":"grafana-postgresql-datasource","info":{"version":"13.0.4"}}`)},
		"MANIFEST.txt":    {Data: []byte("preserved vendor signature provenance")},
		"gpx_linux_amd64": {Data: []byte("synthetic rebuilt executable")},
		"assets/main.js":  {Data: []byte("synthetic existing frontend assets")},
	}
	m := Manifest{Schema: 1, Scope: Scope, KeyID: "isolated-test-v1", Fingerprint: fingerprint, Plugin: "grafana-postgresql-datasource", Version: "13.0.4", Files: map[string]string{}}
	for name, file := range files {
		hash := sha256.Sum256(file.Data)
		m.Files[name] = hex.EncodeToString(hash[:])
	}
	policy := Policy{Plugins: map[string]string{m.Plugin: m.Version}, Keys: map[string]Key{m.KeyID: {Public: pub, Fingerprint: fingerprint}}}
	sign(t, files, m, priv)
	return files, policy, m, priv
}
func sign(t *testing.T, files fstest.MapFS, m Manifest, priv ed25519.PrivateKey) {
	t.Helper()
	payload, err := json.Marshal(m)
	if err != nil {
		t.Fatal(err)
	}
	signRaw(t, files, payload, priv)
}
func signRaw(t *testing.T, files fstest.MapFS, payload []byte, priv ed25519.PrivateKey) {
	t.Helper()
	raw, err := json.Marshal(Envelope{Payload: base64.StdEncoding.EncodeToString(payload), Signature: base64.StdEncoding.EncodeToString(ed25519.Sign(priv, payload))})
	if err != nil {
		t.Fatal(err)
	}
	files[ManifestName] = &fstest.MapFile{Data: raw}
}
func TestValidSyntheticSignature(t *testing.T) {
	files, policy, m, _ := fixture(t)
	handled, err := Verify(files, m.Plugin, m.Version, policy)
	if !handled || err != nil {
		t.Fatalf("valid isolated signature: handled=%v err=%v", handled, err)
	}
}
func TestAbsentTunnerManifestPreservesVendorPath(t *testing.T) {
	files, policy, m, _ := fixture(t)
	delete(files, ManifestName)
	handled, err := Verify(files, m.Plugin, m.Version, policy)
	if handled || err != nil {
		t.Fatalf("vendor path must be delegated, got %v %v", handled, err)
	}
}
func TestPresentInvalidManifestNeverFallsBack(t *testing.T) {
	files, policy, m, _ := fixture(t)
	files[ManifestName].Data = []byte("not a signed manifest")
	handled, err := Verify(files, m.Plugin, m.Version, policy)
	if !handled || err == nil {
		t.Fatal("invalid extension must fail closed")
	}
}
func TestAdversarialMutations(t *testing.T) {
	tests := map[string]func(fstest.MapFS, *Policy, *Manifest, ed25519.PrivateKey){
		"wrong key": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			pub, _, _ := ed25519.GenerateKey(rand.Reader)
			key := p.Keys[m.KeyID]
			key.Public = pub
			p.Keys[m.KeyID] = key
		},
		"unknown key version": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) { delete(p.Keys, m.KeyID) },
		"revoked key version": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			key := p.Keys[m.KeyID]
			key.Revoked = true
			p.Keys[m.KeyID] = key
		},
		"wrong pinned fingerprint": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			key := p.Keys[m.KeyID]
			key.Fingerprint = strings.Repeat("0", 64)
			p.Keys[m.KeyID] = key
		},
		"wrong manifest fingerprint": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			m.Fingerprint = strings.Repeat("0", 64)
			sign(t, f, *m, k)
		},
		"wrong plugin identity": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			m.Plugin = "unapproved-plugin"
			sign(t, f, *m, k)
		},
		"wrong plugin version": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			m.Version = "13.0.5"
			sign(t, f, *m, k)
		},
		"not allowlisted": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) { delete(p.Plugins, m.Plugin) },
		"wrong scope": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			m.Scope = "production"
			sign(t, f, *m, k)
		},
		"wrong schema": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) { m.Schema = 2; sign(t, f, *m, k) },
		"executable tamper": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			f["gpx_linux_amd64"].Data = []byte("tampered")
		},
		"asset tamper": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			f["assets/main.js"].Data = []byte("tampered")
		},
		"vendor manifest tamper": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			f["MANIFEST.txt"].Data = []byte("tampered")
		},
		"missing signed file": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) { delete(f, "gpx_linux_amd64") },
		"extra file": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			f["extra.exe"] = &fstest.MapFile{Data: []byte("injected")}
		},
		"symlink": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			f["gpx_linux_amd64"].Mode = fs.ModeSymlink
		},
		"path traversal": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			m.Files["../escape"] = strings.Repeat("0", 64)
			sign(t, f, *m, k)
		},
		"absolute path": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			m.Files["/escape"] = strings.Repeat("0", 64)
			sign(t, f, *m, k)
		},
		"windows path": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			m.Files["C:\\escape"] = strings.Repeat("0", 64)
			sign(t, f, *m, k)
		},
		"missing vendor provenance": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			delete(m.Files, "MANIFEST.txt")
			delete(f, "MANIFEST.txt")
			sign(t, f, *m, k)
		},
		"malformed hash": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			m.Files["gpx_linux_amd64"] = strings.Repeat("z", 64)
			sign(t, f, *m, k)
		},
		"modified metadata identity": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			f["plugin.json"].Data = []byte(`{"id":"wrong","info":{"version":"13.0.4"}}`)
			sum := sha256.Sum256(f["plugin.json"].Data)
			m.Files["plugin.json"] = hex.EncodeToString(sum[:])
			sign(t, f, *m, k)
		},
		"forged signature": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			var env Envelope
			json.Unmarshal(f[ManifestName].Data, &env)
			env.Signature = base64.StdEncoding.EncodeToString(make([]byte, 64))
			f[ManifestName].Data, _ = json.Marshal(env)
		},
		"duplicate envelope key": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			raw := string(f[ManifestName].Data)
			f[ManifestName].Data = []byte(strings.Replace(raw, "{", `{"payload":"forged",`, 1))
		},
		"duplicate signed field": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			raw, _ := json.Marshal(m)
			signRaw(t, f, []byte(strings.Replace(string(raw), "{", `{"schema":1,`, 1)), k)
		},
		"unknown signed field": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			raw, _ := json.Marshal(m)
			signRaw(t, f, []byte(strings.Replace(string(raw), "{", `{"future_trust":true,`, 1)), k)
		},
		"trailing JSON": func(f fstest.MapFS, p *Policy, m *Manifest, k ed25519.PrivateKey) {
			f[ManifestName].Data = append(f[ManifestName].Data, []byte(" {}")...)
		},
	}
	for name, mutate := range tests {
		t.Run(name, func(t *testing.T) {
			f, p, m, k := fixture(t)
			originalID, originalVersion := m.Plugin, m.Version
			mutate(f, &p, &m, k)
			handled, err := Verify(f, originalID, originalVersion, p)
			if !handled || err == nil {
				t.Fatalf("must reject %s: handled=%v err=%v", name, handled, err)
			}
		})
	}
}
