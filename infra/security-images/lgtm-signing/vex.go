package signing

import (
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"io"
	"sort"
	"strings"
	"time"
)

const tempoDependencyVersion = "v1.5.1-0.20261021152436-9db7df79d2af"
const tempoParentPURL = "pkg:golang/github.com/grafana/grafana-tempo-datasource"

var tempoCVEs = []string{"CVE-2026-21728", "CVE-2026-28377"}
var tempoLinkedPackages = []string{"github.com/grafana/tempo/pkg/tempopb", "github.com/grafana/tempo/pkg/tempopb/common/v1", "github.com/grafana/tempo/pkg/tempopb/resource/v1", "github.com/grafana/tempo/pkg/tempopb/trace/v1"}

type TempoVEXApproval struct {
	ScopeID       string   `json:"scope_id"`
	Answer        string   `json:"answer"`
	Status        string   `json:"status"`
	CVEs          []string `json:"approved_cves"`
	PluginID      string   `json:"plugin_id"`
	PluginVersion string   `json:"plugin_version"`
	BinarySHA256  string   `json:"binary_sha256"`
	SourceCommit  string   `json:"dependency_source_commit"`
	SourceSum     string   `json:"dependency_source_sum"`
	ValidUntil    string   `json:"valid_until_utc"`
}
type TempoVEXBinding struct {
	ScopeID        string `json:"scope_id"`
	VEXSHA256      string `json:"vex_sha256"`
	ApprovalSHA256 string `json:"approval_sha256"`
	ValidUntil     string `json:"valid_until_utc"`
}
type TempoVEXSource struct {
	PluginID           string   `json:"plugin_id"`
	PluginVersion      string   `json:"plugin_version"`
	PluginSourceCommit string   `json:"plugin_source_commit"`
	DependencyVersion  string   `json:"dependency_version"`
	SourceCommit       string   `json:"dependency_source_commit"`
	SourceSum          string   `json:"dependency_source_sum"`
	LinkedPackages     []string `json:"linked_tempo_packages"`
}
type vexComponent struct {
	ID            string            `json:"@id"`
	Hashes        map[string]string `json:"hashes,omitempty"`
	Subcomponents []vexSubcomponent `json:"subcomponents,omitempty"`
}
type vexSubcomponent struct {
	ID string `json:"@id"`
}
type vexStatement struct {
	Vulnerability struct {
		Name string `json:"name"`
	} `json:"vulnerability"`
	Products      []vexComponent `json:"products"`
	Status        string         `json:"status"`
	Justification string         `json:"justification"`
	Impact        string         `json:"impact_statement"`
}
type tempoVEXDocument struct {
	Context    string         `json:"@context"`
	ID         string         `json:"@id"`
	Author     string         `json:"author"`
	Role       string         `json:"role"`
	Timestamp  string         `json:"timestamp"`
	Version    int            `json:"version"`
	Statements []vexStatement `json:"statements"`
}
type vexScan struct {
	ArtifactID string
	Metadata   struct{ ImageID string }
	Results    []struct {
		Target          string
		Vulnerabilities []struct{ VulnerabilityID, PkgName, InstalledVersion string }
	}
}

// DecodeVEXData allows irrelevant scanner/receipt metadata but never duplicate
// keys or trailing values. The signed-plugin manifest decoder remains stricter.
func DecodeVEXData(raw []byte, out any) error {
	d := json.NewDecoder(strings.NewReader(string(raw)))
	if err := uniqueValue(d); err != nil {
		return err
	}
	if _, err := d.Token(); err != io.EOF {
		return errors.New("trailing VEX evidence content")
	}
	return json.Unmarshal(raw, out)
}

// ValidateTempoVEX authorizes only the consented executable/source/expiry and
// exact two findings. It neither changes a scan nor consumes a VEX document.
func ValidateTempoVEX(approval TempoVEXApproval, binding TempoVEXBinding, source TempoVEXSource, binary io.Reader, imageID string, scanJSON, vexJSON []byte, now time.Time) (int, error) {
	reject := func(s string) (int, error) { return 0, errors.New(s) }
	if approval.ScopeID != "P1-RV001-001" || approval.Answer != "Approve these scoped dev/test VEX corrections" || approval.Status != "SCOPE_AUTHORIZED_NOT_GATE_ACCEPTANCE" || !sameStrings(approval.CVEs, tempoCVEs) {
		return reject("missing exact dev/test VEX consent")
	}
	if approval.PluginID != "tempo" || approval.PluginVersion != "13.2.4" || approval.SourceCommit != "9db7df79d2af105a90d96484e6403e1fda0d011e" || approval.SourceSum != "h1:RWARH/pJYWYyhMan2XnvecRlYxnWf4Wa1AmY067hI4I=" {
		return reject("unapproved Tempo source context")
	}
	expiry, err := time.Parse(time.RFC3339, approval.ValidUntil)
	if err != nil || binding.ValidUntil != approval.ValidUntil || !now.Before(expiry) {
		return reject("VEX consent is expired or its expiry changed")
	}
	if binding.ScopeID != approval.ScopeID || source.PluginID != approval.PluginID || source.PluginVersion != approval.PluginVersion || source.PluginSourceCommit != "cdacb742bae90fbf62c48c5cca0eacdc08fd2acd" || source.DependencyVersion != tempoDependencyVersion || source.SourceCommit != approval.SourceCommit || source.SourceSum != approval.SourceSum || !sameStrings(source.LinkedPackages, tempoLinkedPackages) {
		return reject("wrong source/plugin/linked-package context")
	}
	h := sha256.New()
	if _, err = io.Copy(h, binary); err != nil {
		return 0, err
	}
	if hex.EncodeToString(h.Sum(nil)) != approval.BinarySHA256 {
		return reject("rebuilt executable hash differs from reviewed binary")
	}
	docHash := sha256.Sum256(vexJSON)
	if hex.EncodeToString(docHash[:]) != binding.VEXSHA256 {
		return reject("VEX document changed after review")
	}
	var doc tempoVEXDocument
	if err = strictJSON(vexJSON, &doc); err != nil {
		return 0, err
	}
	if doc.Context != "https://openvex.dev/ns/v0.2.0" || doc.Author != "Tunner engineering proposal" || doc.Role != "Tunner dev/test applicability assessment" || doc.Version != 1 || len(doc.Statements) != 2 {
		return reject("unreviewed VEX document shape")
	}
	seen := map[string]bool{}
	for _, s := range doc.Statements {
		id := s.Vulnerability.Name
		if !contains(tempoCVEs, id) || seen[id] || s.Status != "not_affected" || s.Justification != "vulnerable_code_not_present" || s.Impact == "" || len(s.Products) != 1 {
			return reject("unapproved VEX CVE/status/justification")
		}
		seen[id] = true
		p := s.Products[0]
		if p.ID != tempoParentPURL || len(p.Hashes) != 1 || p.Hashes["sha-256"] != approval.BinarySHA256 || len(p.Subcomponents) != 1 || p.Subcomponents[0].ID != "pkg:golang/github.com/grafana/tempo@"+tempoDependencyVersion {
			return reject("unapproved VEX product/package/hash")
		}
	}
	var scan vexScan
	if err = DecodeVEXData(scanJSON, &scan); err != nil {
		return 0, err
	}
	if imageID == "" || (imageID != scan.ArtifactID && imageID != scan.Metadata.ImageID) {
		return reject("scan and extracted image identities differ")
	}
	count := 0
	seenFinding := map[string]bool{}
	targets := map[string]bool{}
	for _, r := range scan.Results {
		for _, v := range r.Vulnerabilities {
			if v.PkgName != "github.com/grafana/tempo" || v.InstalledVersion != tempoDependencyVersion || !contains(tempoCVEs, v.VulnerabilityID) {
				continue
			}
			if r.Target != "plugins/tempo/gpx_grafana-tempo-datasource_linux_amd64" && r.Target != "otel-lgtm/grafana/data/plugins-bundled/tempo/gpx_grafana-tempo-datasource_linux_amd64" {
				return reject("same VEX package appears outside the approved executable context")
			}
			key := r.Target + "/" + v.VulnerabilityID
			if seenFinding[key] {
				return reject("duplicate scanner evidence")
			}
			seenFinding[key] = true
			targets[r.Target] = true
			count++
		}
	}
	if len(targets) > 1 {
		return reject("more than one matching executable context")
	}
	return count, nil
}
func contains(values []string, v string) bool {
	for _, s := range values {
		if s == v {
			return true
		}
	}
	return false
}
func sameStrings(a, b []string) bool {
	if len(a) != len(b) {
		return false
	}
	x := append([]string{}, a...)
	y := append([]string{}, b...)
	sort.Strings(x)
	sort.Strings(y)
	for i := range x {
		if x[i] != y[i] {
			return false
		}
	}
	return true
}

type TempoVEXReviews struct {
	ScopeID               string            `json:"scope_id"`
	Result                string            `json:"result"`
	Roles                 []string          `json:"roles"`
	ValidatorSourceSHA256 string            `json:"validator_source_sha256"`
	EvidenceHashes        map[string]string `json:"evidence_sha256"`
}

func ValidateTempoVEXReviews(review TempoVEXReviews, expected map[string]string, codeHash string) error {
	if review.ScopeID != "P1-RV001-001" || review.Result != "PASS_SCOPED_DEV_TEST_VEX" || !sameStrings(review.Roles, []string{"solution-architect", "cyber-security-engineer", "governance-engineer"}) {
		return errors.New("required scoped specialist reviews are missing")
	}
	if codeHash == "" || review.ValidatorSourceSHA256 != codeHash {
		return errors.New("validator source changed after review")
	}
	for _, key := range []string{"approval", "binding", "source", "vex", "tests"} {
		if expected[key] == "" || review.EvidenceHashes[key] != expected[key] {
			return errors.New("reviewed evidence changed: " + key)
		}
	}
	return nil
}
