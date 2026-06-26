# Entra External ID app registrations (`infra/entra`)

Terraform for the **Entra External ID (CIAM)** app registrations that back real
OIDC login ([#56](https://github.com/abrahamFerga/NutriForge/issues/56)): an
**API** registration (exposes the `access_as_user` scope + `admin` app role) and
a **SPA** registration (public client, Auth-Code + PKCE).

This is a **separate root module** from [`../`](../) (the subscription infra)
because the registrations live in the **CIAM tenant**, a different tenant from
the Azure subscription that hosts Container Apps / Postgres / Redis. Different
tenant ⇒ different provider auth ⇒ its own state.

## What it does NOT do

- **It does not create the CIAM tenant.** Terraform can't reliably provision an
  Entra External ID tenant — create it once by hand (below), then point this
  module at it.
- **It does not create the user flow.** External ID's sign-up/sign-in user flow
  and its branding are configured in the portal; associate the SPA app with the
  user flow there.

## One-time tenant setup (manual)

1. In the Azure portal, create an **Entra External ID** tenant (Microsoft Entra
   → *External Identities* → create an external tenant). Note its **tenant ID**
   (a GUID) and **subdomain** (the part before `.ciamlogin.com`).
2. Create a **sign-up and sign-in user flow** in that tenant.
3. `az login --tenant <CIAM_TENANT_GUID>` so Terraform authenticates against the
   CIAM tenant (not your subscription's home tenant).

## Apply

```bash
cd infra/entra
cp terraform.tfvars.example terraform.tfvars   # fill in tenant_id, subdomain, SPA origins
terraform init -backend=false                  # local state; add a backend for shared use
terraform apply
```

After apply, associate the **spa** app registration with the user flow from
step 2 in the portal.

## Wire the outputs into the app

`terraform output` prints everything you need:

| Output           | Where it goes                                                            |
|------------------|--------------------------------------------------------------------------|
| `auth_authority` | `infra/environments/<env>.tfvars` → `auth_authority` (API env)           |
| `auth_audience`  | `infra/environments/<env>.tfvars` → `auth_audience` (API env)            |
| `vite_auth_env`  | the SPA build environment (`.env` / CI) — see `src/NutriForge.Web/.env.example` |

The subscription infra injects `Authentication__Authority` / `Authentication__Audience`
into the API Container App from those two tfvars values (see `../main.tf`). With
them set, the API validates real Entra tokens and the dev-auth header bypass is
off (it is refused outside Development regardless — fail-closed).

The SPA flips from dev-auth to MSAL login automatically once the three
`VITE_AUTH_*` values are present at build time (see `src/NutriForge.Web/src/lib/auth.ts`).

## Admin role

The API exposes an `admin` app role that maps to the in-app `RequireRole(admin)`
policy. Either set `admin_principal_object_ids` (prefer a group) before apply, or
assign the role later in the portal (*Enterprise applications → nutriforge-api →
Users and groups*).
