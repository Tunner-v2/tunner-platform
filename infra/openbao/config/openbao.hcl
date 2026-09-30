# Local single-node OpenBao transport/storage configuration only.
# P0-007 owns initialization, unseal handling, policy, and secret injection.
ui = false
disable_mlock = true

storage "file" {
  path = "/openbao/file"
}

listener "tcp" {
  address     = "0.0.0.0:8200"
  tls_disable = 1
}

api_addr = "http://openbao:8200"
