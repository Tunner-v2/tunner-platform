# P0 local runtime policy. No token is created or persisted by this repository.
# The policy can only read references beneath the local Tunner KV v2 mount.
path "tunner/data/local/*" {
  capabilities = ["read"]
}

path "tunner/metadata/local/*" {
  capabilities = ["list"]
}