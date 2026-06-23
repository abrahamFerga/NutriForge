###############################################################################
# backend.tf
#
# Remote state in an Azure Storage container (the azurerm backend).
#
# The storage account, container, and the RBAC grant that lets CI write state
# must EXIST BEFORE the first `terraform init` (bootstrap them once, by hand or
# with a tiny separate root). Terraform cannot create the backend that stores
# its own state.
#
# These values are intentionally left as placeholders. Do NOT hardcode an
# environment-specific `key` here -- pass per-environment backend config at
# init time so dev and prod use separate state blobs:
#
#   terraform init \
#     -backend-config="key=dev.tfstate"          # or prod.tfstate
#
# Authentication uses Entra ID (use_azuread_auth) + OIDC in CI -- no storage
# account keys. Grant the CI identity "Storage Blob Data Contributor" on the
# state container.
###############################################################################

terraform {
  backend "azurerm" {
    resource_group_name  = "<TFSTATE_RESOURCE_GROUP>"  # e.g. rg-tfstate
    storage_account_name = "<TFSTATE_STORAGE_ACCOUNT>" # globally unique, 3-24 lowercase
    container_name       = "<TFSTATE_CONTAINER>"       # e.g. tfstate
    key                  = "dev.tfstate"               # override per env via -backend-config

    use_azuread_auth = true # data-plane auth via Entra ID, not account keys
    use_oidc         = true # reuse the GitHub OIDC token in CI
  }
}
