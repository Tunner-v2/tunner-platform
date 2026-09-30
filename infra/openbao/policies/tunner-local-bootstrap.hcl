# P0 local bootstrap policy. It can provision only the Tunner local KV mount
# and the companion runtime policy; it cannot read application secret values.
path "sys/mounts" {
  capabilities = ["read"]
}

path "sys/mounts/tunner" {
  capabilities = ["create", "update", "read"]
}

path "sys/policies/acl/tunner-local-runtime" {
  capabilities = ["create", "update", "read"]
}