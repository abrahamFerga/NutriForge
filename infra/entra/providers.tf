###############################################################################
# infra/entra/providers.tf
#
# This is a SEPARATE Terraform root module from ../ (the subscription infra).
#
# Why separate: the Entra app registrations created here live in the *Entra
# External ID (CIAM) tenant*, which is a DIFFERENT tenant from the Azure
# subscription where ACA / Postgres / Redis are provisioned. They use different
# provider auth and have a different lifecycle, so they get their own state.
#
# Auth model:
#   - Locally: `az login --tenant <ciam-tenant-id>` then run terraform; the
#     azuread provider falls back to Azure CLI auth against that tenant.
#   - In CI: OIDC / Workload Identity Federation against the CIAM tenant
#     (set ARM_TENANT_ID/ARM_CLIENT_ID for the CIAM app and ARM_USE_OIDC=true).
#
# The Entra External ID *tenant itself* is NOT created here — Terraform cannot
# reliably provision a CIAM tenant. Create it once by hand (see README.md),
# then point this module at it via var.tenant_id / var.tenant_subdomain.
###############################################################################

terraform {
  required_version = ">= 1.9.0"

  required_providers {
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
  }

  # Remote state is optional for this module. Uncomment and pass -backend-config
  # at init time to store it alongside the subscription state (a separate key):
  #
  #   terraform init -backend-config="key=entra.tfstate" ...
  #
  # backend "azurerm" {
  #   use_azuread_auth = true
  #   use_oidc         = true
  # }
}

provider "azuread" {
  # The CIAM tenant the app registrations are created in. Required — this is what
  # makes the registrations land in the External ID tenant rather than the
  # subscription's home tenant.
  tenant_id = var.tenant_id

  # Honor OIDC when ARM_USE_OIDC=true is exported (CI). Local runs fall back to
  # Azure CLI auth from `az login --tenant <tenant_id>`.
  use_oidc = true
}
