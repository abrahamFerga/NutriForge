###############################################################################
# providers.tf
#
# Pins Terraform + the azurerm/azuread providers and configures provider auth.
#
# Auth model:
#   - In GitHub Actions, authentication is via OIDC / Workload Identity
#     Federation. azure/login@v2 logs in first, and the azurerm provider then
#     reuses that login because `use_oidc = true` plus the ARM_* env vars
#     (ARM_CLIENT_ID / ARM_TENANT_ID / ARM_SUBSCRIPTION_ID / ARM_USE_OIDC) are
#     exported by the workflow. No client secret is ever stored.
#   - Locally, run `az login` and the provider falls back to Azure CLI auth.
###############################################################################

terraform {
  # Pin the CLI floor; CI installs an exact version via setup-terraform.
  required_version = ">= 1.9.0"

  required_providers {
    azurerm = {
      source = "hashicorp/azurerm"
      # azurerm v4 is the current major. Stay on the latest 4.x patch.
      version = "~> 4.0"
    }
    azuread = {
      source  = "hashicorp/azuread"
      version = "~> 3.0"
    }
  }
}

provider "azurerm" {
  # The features {} block is REQUIRED by the azurerm provider, even when empty.
  features {}

  # Honor OIDC when ARM_USE_OIDC=true is exported by the workflow.
  use_oidc = true

  # subscription_id is taken from ARM_SUBSCRIPTION_ID. Set it explicitly here
  # only if you are NOT exporting that env var.
  # subscription_id = "<SUBSCRIPTION_ID>"
}

provider "azuread" {
  use_oidc = true
}
