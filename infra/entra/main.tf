###############################################################################
# infra/entra/main.tf
#
# Entra External ID (CIAM) app registrations for NutriForge (#56):
#
#   API app registration        — exposes the `access_as_user` delegated scope
#                                  and the `admin` app role; its client ID is the
#                                  token audience the API validates.
#   SPA app registration         — public client (Auth-Code + PKCE), requests the
#                                  API scope; pre-authorized so users skip consent.
#
# Token flow: the SPA signs the user in (MSAL), requests
# `api://<api-client-id>/access_as_user`, and calls the API with the resulting
# access token. The API validates `iss` (the CIAM authority) and `aud` (its own
# client ID), and maps the `roles` claim to RequireRole(admin).
#
# Fixed permission/role GUIDs: the scope and app-role IDs are hardcoded (not
# random) so they stay stable across applies — other resources reference them.
###############################################################################

data "azuread_client_config" "current" {}

# Microsoft Graph well-known app ID + the standard OIDC delegated-permission IDs
# (constant across all tenants) the SPA needs for sign-in + silent token refresh.
locals {
  microsoft_graph_app_id = "00000003-0000-0000-c000-000000000000"
  graph_scope_openid     = "37f7f235-527c-4136-accd-4a02d197296e"
  graph_scope_offline    = "7427e0e9-2fba-42fe-b0c0-848c9e6a8182"
  graph_scope_profile    = "14dad69e-099b-42c9-810b-d002981feec1"

  # Stable IDs for the permissions this module defines on the API app.
  scope_access_as_user_id = "5f9a1b2c-3d4e-4f50-9a1b-2c3d4e5f6071"
  role_admin_id           = "7c1e2d3f-4a5b-4c6d-8e9f-0a1b2c3d4e5f"
}

# --- API app registration --------------------------------------------------- #
resource "azuread_application" "api" {
  display_name     = "${var.app_name}-api"
  sign_in_audience = "AzureADMyOrg" # single CIAM tenant
  owners           = [data.azuread_client_config.current.object_id]

  api {
    requested_access_token_version = 2 # v2 tokens (sub/roles claims the API maps)

    oauth2_permission_scope {
      id                         = local.scope_access_as_user_id
      value                      = "access_as_user"
      type                       = "User" # user-consentable delegated scope
      enabled                    = true
      admin_consent_display_name = "Access NutriForge as the signed-in user"
      admin_consent_description  = "Allow the app to call the NutriForge API on behalf of the signed-in user."
      user_consent_display_name  = "Access NutriForge on your behalf"
      user_consent_description   = "Allow the app to call NutriForge as you."
    }
  }

  # The `admin` app role backs the RequireRole(admin) RBAC policy. Assignable to
  # users and groups (see azuread_app_role_assignment below).
  app_role {
    id                   = local.role_admin_id
    value                = "admin"
    display_name         = "Administrator"
    description          = "Full administrative access (maps to the in-app admin RBAC policy)."
    allowed_member_types = ["User"]
    enabled              = true
  }
}

# Set the App ID URI to api://<client-id> in a second step. Doing it on the
# application resource itself would be a cycle (the URI references the client ID
# the resource is still creating); the dedicated resource breaks that.
resource "azuread_application_identifier_uri" "api" {
  application_id = azuread_application.api.id
  identifier_uri = "api://${azuread_application.api.client_id}"
}

resource "azuread_service_principal" "api" {
  client_id = azuread_application.api.client_id
  owners    = [data.azuread_client_config.current.object_id]
}

# --- SPA app registration (public client, Auth-Code + PKCE) ----------------- #
resource "azuread_application" "spa" {
  display_name     = "${var.app_name}-spa"
  sign_in_audience = "AzureADMyOrg"
  owners           = [data.azuread_client_config.current.object_id]

  single_page_application {
    redirect_uris = var.spa_redirect_uris
  }

  # The API scope the SPA requests on the user's behalf...
  required_resource_access {
    resource_app_id = azuread_application.api.client_id

    resource_access {
      id   = local.scope_access_as_user_id
      type = "Scope"
    }
  }

  # ...plus the standard OIDC scopes for sign-in and silent refresh.
  required_resource_access {
    resource_app_id = local.microsoft_graph_app_id

    resource_access {
      id   = local.graph_scope_openid
      type = "Scope"
    }
    resource_access {
      id   = local.graph_scope_offline
      type = "Scope"
    }
    resource_access {
      id   = local.graph_scope_profile
      type = "Scope"
    }
  }
}

resource "azuread_service_principal" "spa" {
  client_id = azuread_application.spa.client_id
  owners    = [data.azuread_client_config.current.object_id]
}

# Pre-authorize the SPA on the API scope so first-party users are not prompted
# to consent to our own API.
resource "azuread_application_pre_authorized" "spa_on_api" {
  application_id       = azuread_application.api.id
  authorized_client_id = azuread_application.spa.client_id
  permission_ids       = [local.scope_access_as_user_id]
}

# --- Admin role assignments (optional) -------------------------------------- #
# Assign the API's `admin` app role to the configured principals. The assignment
# lives on the API's service principal (the resource exposing the role).
resource "azuread_app_role_assignment" "admin" {
  for_each = toset(var.admin_principal_object_ids)

  app_role_id         = local.role_admin_id
  principal_object_id = each.value
  resource_object_id  = azuread_service_principal.api.object_id
}
