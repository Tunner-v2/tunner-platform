// rebuild-plugins reproduces exact vendor plugin sources with bounded security
// dependency/toolchain repairs. It emits unsigned candidate assets/provenance;
// it does not activate trust, publish plugins or assert complete LGTM acceptance.
package main

import (
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"io/fs"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
)

type plugin struct {
	Directory     string `json:"directory"`
	ID            string `json:"id"`
	Version       string `json:"version"`
	Path          string `json:"path"`
	Module        string `json:"module"`
	SourceVersion string `json:"source_version"`
	SourceSum     string `json:"source_sum"`
	GoModSum      string `json:"go_mod_sum"`
	VCS           string `json:"vcs"`
	Binary        string `json:"binary"`
}
type sourceInventory struct {
	Scope   string   `json:"scope"`
	Plugins []plugin `json:"plugins"`
}

func main() {
	if err := run(); err != nil {
		fmt.Fprintln(os.Stderr, err)
		os.Exit(1)
	}
}
func run() error {
	inventoryPath := flag.String("inventory", "", "reviewed source inventory")
	vendorRoot := flag.String("vendor-root", "", "unchanged vendor plugin assets")
	output := flag.String("output", "/out", "candidate output")
	selected := flag.String("plugin", "", "optional exact plugin ID for a bounded candidate")
	flag.Parse()
	raw, err := os.ReadFile(*inventoryPath)
	if err != nil {
		return err
	}
	var inventory sourceInventory
	if err = json.Unmarshal(raw, &inventory); err != nil {
		return err
	}
	if inventory.Scope != "DEV_TEST_ONLY_NOT_YET_ADOPTED" || len(inventory.Plugins) != 13 {
		return errors.New("unexpected reviewed dev/test source inventory")
	}
	count := 0
	for _, p := range inventory.Plugins {
		if *selected != "" && p.ID != *selected {
			continue
		}
		if !fs.ValidPath(p.Directory) || strings.Contains(p.Directory, "/") || !fs.ValidPath(p.Binary) || strings.Contains(p.Binary, "/") || !strings.HasPrefix(p.Module, "github.com/grafana/") || !strings.HasPrefix(p.Path, p.Module+"/") {
			return errors.New("unsafe plugin source entry")
		}
		fmt.Println("REBUILD:", p.ID, p.Version, p.VCS)
		if err := rebuild(p, *vendorRoot, *output); err != nil {
			return fmt.Errorf("plugin %s: %w", p.ID, err)
		}
		count++
	}
	if count == 0 {
		return errors.New("no exact reviewed plugin selected")
	}
	proof, _ := json.MarshalIndent(map[string]any{"scope": inventory.Scope, "rebuilt_plugins": count, "complete_lgtm": false, "runtime_adoption": false, "signing": "Not signed; mandatory separate signature/compatibility/full-stack gates remain."}, "", "  ")
	return os.WriteFile(filepath.Join(*output, "candidate-boundary.json"), append(proof, '\n'), 0444)
}
func command(dir, name string, args ...string) error {
	cmd := exec.Command(name, args...)
	cmd.Dir = dir
	cmd.Stdout = os.Stdout
	cmd.Stderr = os.Stderr
	return cmd.Run()
}
func capture(dir string, args ...string) ([]byte, error) {
	cmd := exec.Command("go", args...)
	cmd.Dir = dir
	cmd.Stderr = os.Stderr
	return cmd.Output()
}
func rebuild(p plugin, vendorRoot, output string) error {
	data, err := capture("", "mod", "download", "-json", p.Module+"@"+p.SourceVersion)
	if err != nil {
		return err
	}
	var module struct{ Dir, Sum, GoModSum string }
	if err = json.Unmarshal(data, &module); err != nil {
		return err
	}
	if module.Sum != p.SourceSum || module.GoModSum != p.GoModSum || !strings.HasPrefix(module.Dir, "/go/pkg/mod/") {
		return errors.New("downloaded source checksum/location differs from approved inventory")
	}
	work := filepath.Join("/work/plugins", p.Directory)
	assets := filepath.Join(output, "plugins", p.Directory)
	proof := filepath.Join(output, "provenance", p.Directory)
	for _, dir := range []string{work, assets, proof} {
		if err := os.MkdirAll(dir, 0755); err != nil {
			return err
		}
	}
	if err = command("", "cp", "-a", module.Dir+"/.", work); err != nil {
		return err
	}
	if err = command("", "chmod", "-R", "u+w", work); err != nil {
		return err
	}
	if err = command("", "cp", "-a", filepath.Join(vendorRoot, p.Directory)+"/.", assets); err != nil {
		return err
	}
	patches := []string{"golang.org/x/net@v0.60.0"}
	if p.ID == "stackdriver" {
		patches = append(patches, "google.golang.org/grpc@v1.83.2")
	}
	if p.ID == "tempo" {
		patches = append(patches, "github.com/grafana/tempo@9db7df79d2af105a90d96484e6403e1fda0d011e")
	}
	if err = command(work, "go", append([]string{"get"}, patches...)...); err != nil {
		return err
	}
	if err = command(work, "go", "mod", "verify"); err != nil {
		return err
	}
	tests, err := os.Create(filepath.Join(proof, "upstream-tests.json"))
	if err != nil {
		return err
	}
	test := exec.Command("go", "test", "-p=2", "-count=1", "-json", "./pkg/...")
	test.Dir = work
	test.Stdout = tests
	test.Stderr = os.Stderr
	testErr := test.Run()
	closeErr := tests.Close()
	if testErr != nil {
		reportTestFailure(filepath.Join(proof, "upstream-tests.json"))
		return testErr
	}
	if closeErr != nil {
		return closeErr
	}
	info, _ := json.Marshal(map[string]any{"time": int64(1792454400000), "pluginID": p.ID, "version": p.Version})
	flags := fmt.Sprintf("-X 'github.com/grafana/grafana-plugin-sdk-go/build/buildinfo.buildInfoJSON=%s' -X main.pluginID=%s -X main.version=%s", info, p.ID, p.Version)
	relative := "." + strings.TrimPrefix(p.Path, p.Module)
	if err = command(work, "go", "build", "-p=2", "-trimpath", "-buildvcs=false", "-ldflags", flags, "-o", filepath.Join(assets, p.Binary), relative); err != nil {
		return err
	}
	for _, name := range []string{"go.mod", "go.sum"} {
		body, err := os.ReadFile(filepath.Join(work, name))
		if err != nil {
			return err
		}
		if err = os.WriteFile(filepath.Join(proof, name), body, 0444); err != nil {
			return err
		}
	}
	buildInfo, err := capture(work, "version", "-m", filepath.Join(assets, p.Binary))
	if err != nil {
		return err
	}
	if err = os.WriteFile(filepath.Join(proof, "buildinfo.txt"), buildInfo, 0444); err != nil {
		return err
	}
	modules, err := capture(work, "list", "-m", "-json", "all")
	if err != nil {
		return err
	}
	if err = os.WriteFile(filepath.Join(proof, "modules.json"), modules, 0444); err != nil {
		return err
	}
	return os.WriteFile(filepath.Join(proof, "upstream-source.json"), append(data, '\n'), 0444)
}

// Retain full JSON on disk and emit only failing-test diagnostics to build logs.
func reportTestFailure(name string) {
 f,err:=os.Open(name);if err!=nil{return};defer f.Close()
 type event struct{Action,Package,Test,Output string}
 decoder:=json.NewDecoder(f);outputs:=map[string][]string{};failed:=[]string{}
 for {var e event;if decoder.Decode(&e)!=nil{break};key:=e.Package+"/"+e.Test
  if e.Output!="" {outputs[key]=append(outputs[key],e.Output);if len(outputs[key])>32{outputs[key]=outputs[key][len(outputs[key])-32:]}}
  if e.Action=="fail"{failed=append(failed,key)}
 }
 for _,key:=range failed{fmt.Fprintln(os.Stderr,"UPSTREAM TEST FAILURE:",key);for _,line:=range outputs[key]{fmt.Fprint(os.Stderr,line)}}
}
