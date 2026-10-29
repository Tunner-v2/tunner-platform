# Invoke from the resolved repository root. All paths remain repository relative.
variable "UI_AUDIT_EPOCH" { default = "required" }
variable "RECIPE_SHA256" { default = "unbound-not-accepted" }
group "default" { targets = ["lgtm", "provenance"] }
target "common" {
  context = "infra/security-images/lgtm-signing"
  platforms = ["linux/amd64"]
}
target "grafana" {
  inherits = ["common"]
  dockerfile = "grafana.Dockerfile"
}
target "tempo" {
  inherits = ["common"]
  dockerfile = "tempo.Dockerfile"
}
target "pyroscope" {
  inherits = ["common"]
  dockerfile = "pyroscope.Dockerfile"
  args = { UI_AUDIT_EPOCH = UI_AUDIT_EPOCH }
}
target "obi" {
  inherits = ["common"]
  dockerfile = "collector-obi.Dockerfile"
  target = "obi-candidate"
}
target "collector" {
  inherits = ["common"]
  dockerfile = "collector-obi.Dockerfile"
  target = "collector-candidate"
}
target "lgtm" {
  inherits = ["common"]
  dockerfile = "runtime.Dockerfile"
  target = "runtime"
  contexts = { grafana = "target:grafana", tempo = "target:tempo", pyroscope = "target:pyroscope", obi = "target:obi", collector = "target:collector" }
  tags = ["tunner-security/lgtm:0.38.1-r1"]
  labels = { "io.tunner.recipe.sha256" = RECIPE_SHA256 }
  output = ["type=docker"]
}
target "provenance" {
  inherits = ["common"]
  dockerfile = "runtime.Dockerfile"
  target = "provenance"
  contexts = { grafana = "target:grafana", tempo = "target:tempo", pyroscope = "target:pyroscope", obi = "target:obi", collector = "target:collector" }
  output = ["type=local,dest=artifacts/security-images/lgtm-provenance"]
}
