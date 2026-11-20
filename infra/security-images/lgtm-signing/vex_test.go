package signing

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"strings"
	"testing"
	"time"
)

func vexHash(data []byte) string { s := sha256.Sum256(data); return hex.EncodeToString(s[:]) }
func vexFixture() (TempoVEXApproval, TempoVEXBinding, TempoVEXSource, tempoVEXDocument, []byte, string) {
	binary := "synthetic reviewed executable"
	a := TempoVEXApproval{ScopeID: "P1-RV001-001", Answer: "Approve these scoped dev/test VEX corrections", Status: "SCOPE_AUTHORIZED_NOT_GATE_ACCEPTANCE", CVEs: append([]string{}, tempoCVEs...), PluginID: "tempo", PluginVersion: "13.2.4", BinarySHA256: vexHash([]byte(binary)), SourceCommit: "9db7df79d2af105a90d96484e6403e1fda0d011e", SourceSum: "h1:RWARH/pJYWYyhMan2XnvecRlYxnWf4Wa1AmY067hI4I=", ValidUntil: "2026-11-27T00:00:00Z"}
	b := TempoVEXBinding{ScopeID: a.ScopeID, ValidUntil: a.ValidUntil}
	s := TempoVEXSource{PluginID: a.PluginID, PluginVersion: a.PluginVersion, PluginSourceCommit: "cdacb742bae90fbf62c48c5cca0eacdc08fd2acd", DependencyVersion: tempoDependencyVersion, SourceCommit: a.SourceCommit, SourceSum: a.SourceSum, LinkedPackages: append([]string{}, tempoLinkedPackages...)}
	doc := tempoVEXDocument{Context: "https://openvex.dev/ns/v0.2.0", ID: "urn:tunner:proposal:vex:tempo-plugin:20261028", Author: "Tunner engineering proposal", Role: "Tunner dev/test applicability assessment", Timestamp: "2026-10-28T20:00:00Z", Version: 1}
	for _, id := range tempoCVEs {
		var st vexStatement
		st.Vulnerability.Name = id
		st.Status = "not_affected"
		st.Justification = "vulnerable_code_not_present"
		st.Impact = "Synthetic unit fixture only"
		st.Products = []vexComponent{{ID: tempoParentPURL, Hashes: map[string]string{"sha-256": a.BinarySHA256}, Subcomponents: []vexSubcomponent{{ID: "pkg:golang/github.com/grafana/tempo@" + tempoDependencyVersion}}}}
		doc.Statements = append(doc.Statements, st)
	}
	raw := []byte(`{"ArtifactID":"sha256:fixture","Metadata":{"ImageID":"sha256:fixture"},"Results":[{"Target":"plugins/tempo/gpx_grafana-tempo-datasource_linux_amd64","Vulnerabilities":[{"VulnerabilityID":"CVE-2026-21728","PkgName":"github.com/grafana/tempo","InstalledVersion":"v1.5.1-0.20261021152436-9db7df79d2af"},{"VulnerabilityID":"CVE-2026-28377","PkgName":"github.com/grafana/tempo","InstalledVersion":"v1.5.1-0.20261021152436-9db7df79d2af"}]}]}`)
	return a, b, s, doc, raw, binary
}
func TestTempoVEXExactBinding(t *testing.T) {
	a, b, s, d, scan, binary := vexFixture()
	doc, _ := json.Marshal(d)
	b.VEXSHA256 = vexHash(doc)
	count, err := ValidateTempoVEX(a, b, s, strings.NewReader(binary), "sha256:fixture", scan, doc, time.Date(2026, 10, 28, 21, 0, 0, 0, time.UTC))
	if err != nil || count != 2 {
		t.Fatalf("expected exact two reviewed findings, got %d %v", count, err)
	}
}
func TestTempoVEXRejections(t *testing.T) {
	cases := map[string]func(*TempoVEXApproval, *TempoVEXBinding, *TempoVEXSource, *tempoVEXDocument, *[]byte, *string, *time.Time){
		"wrong source": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			s.SourceCommit = "different"
		},
		"wrong source sum": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			s.SourceSum = "different"
		},
		"wrong binary hash": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			*bin = "modified executable"
		},
		"wrong CVE": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			d.Statements[0].Vulnerability.Name = "CVE-UNKNOWN"
		},
		"expanded consent": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			a.CVEs = append(a.CVEs, "CVE-UNKNOWN")
		},
		"expired consent": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			*n = time.Date(2026, 11, 27, 0, 0, 0, 0, time.UTC)
		},
		"extended expiry": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			b.ValidUntil = "2026-12-27T00:00:00Z"
		},
		"missing human consent": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			a.Answer = "proceed"
		},
		"wrong scope": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			b.ScopeID = "different"
		},
		"wrong plugin": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			s.PluginID = "different"
		},
		"wrong version": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			s.PluginVersion = "13.2.5"
		},
		"linked vulnerable server code": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			s.LinkedPackages = append(s.LinkedPackages, "github.com/grafana/tempo/modules/frontend")
		},
		"missing linked package evidence": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			s.LinkedPackages = nil
		},
		"wrong target": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			*r = []byte(strings.ReplaceAll(string(*r), "plugins/tempo/", "unapproved/"))
		},
		"wrong image": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			*r = []byte(strings.ReplaceAll(string(*r), "sha256:fixture", "sha256:other"))
		},
		"wrong package in VEX": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			d.Statements[0].Products[0].Subcomponents[0].ID = "pkg:golang/other@1"
		},
		"wrong product in VEX": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			d.Statements[0].Products[0].ID = "pkg:golang/other"
		},
		"wrong hash in VEX": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			d.Statements[0].Products[0].Hashes["sha-256"] = "different"
		},
		"wrong justification": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			d.Statements[0].Justification = "component_not_present"
		},
		"draft document": func(a *TempoVEXApproval, b *TempoVEXBinding, s *TempoVEXSource, d *tempoVEXDocument, r *[]byte, bin *string, n *time.Time) {
			d.Role = "DRAFT_NOT_APPROVED"
		},
	}
	for name, mutate := range cases {
		t.Run(name, func(t *testing.T) {
			a, b, s, d, raw, binary := vexFixture()
			now := time.Date(2026, 10, 28, 21, 0, 0, 0, time.UTC)
			mutate(&a, &b, &s, &d, &raw, &binary, &now)
			doc, _ := json.Marshal(d)
			b.VEXSHA256 = vexHash(doc)
			if _, err := ValidateTempoVEX(a, b, s, strings.NewReader(binary), "sha256:fixture", raw, doc, now); err == nil {
				t.Fatalf("must reject %s", name)
			}
		})
	}
}
func TestTempoVEXRejectsDocumentTampering(t *testing.T) {
	a, b, s, d, scan, binary := vexFixture()
	doc, _ := json.Marshal(d)
	b.VEXSHA256 = vexHash(doc)
	doc = append(doc, ' ')
	if _, err := ValidateTempoVEX(a, b, s, strings.NewReader(binary), "sha256:fixture", scan, doc, time.Date(2026, 10, 28, 21, 0, 0, 0, time.UTC)); err == nil {
		t.Fatal("changed VEX bytes must reject")
	}
}

func TestTempoVEXReviewRejections(t *testing.T) {
	expected := map[string]string{"approval": "a", "binding": "b", "source": "s", "vex": "v", "tests": "t"}
	makeReview := func() TempoVEXReviews {
		return TempoVEXReviews{ScopeID: "P1-RV001-001", Result: "PASS_SCOPED_DEV_TEST_VEX", Roles: []string{"solution-architect", "cyber-security-engineer", "governance-engineer"}, ValidatorSourceSHA256: "code", EvidenceHashes: map[string]string{"approval": "a", "binding": "b", "source": "s", "vex": "v", "tests": "t"}}
	}
	if err := ValidateTempoVEXReviews(makeReview(), expected, "code"); err != nil {
		t.Fatal(err)
	}
	r := makeReview()
	r.Roles = nil
	if ValidateTempoVEXReviews(r, expected, "code") == nil {
		t.Fatal("missing reviews must reject")
	}
	r = makeReview()
	r.ValidatorSourceSHA256 = "modified"
	if ValidateTempoVEXReviews(r, expected, "code") == nil {
		t.Fatal("changed validator must reject")
	}
	r = makeReview()
	r.EvidenceHashes["vex"] = "changed"
	if ValidateTempoVEXReviews(r, expected, "code") == nil {
		t.Fatal("changed reviewed VEX must reject")
	}
	r = makeReview()
	r.Result = "DRAFT"
	if ValidateTempoVEXReviews(r, expected, "code") == nil {
		t.Fatal("unapproved review must reject")
	}
}
