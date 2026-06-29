# Quartas de Final (F2) — Gateway YARP + identidade dois-mundos

Você coloca **identidade** na frente do FIFA 2026 Tickets: sobe o **gateway YARP** (a porta da frente), faz ele validar **dois mundos de login** com a mesma mecânica (issuer-agnóstico) e, no fim, **migra** usuários antigos (senha bcrypt) para o CIAM **sem apagar nada** — provando que as duas identidades convivem na mesma linha do banco.

| | **Cliente (comprador)** | **Admin (operador)** |
|---|---|---|
| Produto | **Entra External ID** (CIAM) | **Entra ID** (workforce) |
| URL de login | **`<seu-tenant>.ciamlogin.com`** | **`login.microsoftonline.com`** |
| Como entra | cadastro self-service (Google / email+OTP) | conta corporativa que já existe |

> **Story:** [2.11](../stories/2.11.story.md) · **Decisões:** [ADE-007 v1.1](../architecture/ade-007-identity-external-id.md) · [ADE-004 gateway issuer-agnóstico](../architecture/ade-004-yarp-gateway.md) · **Workflow:** [`lab-quartas-de-final.yml`](../../.github/workflows/lab-quartas-de-final.yml)

> **Regra de ouro (vale para o guia inteiro):** o **Portal / Entra / Google** criam e configuram **toda a infra e identidade à mão** (Fases 1–4). O **GitHub Actions é o ÚLTIMO passo** (Fase 5) e só publica **código** (gateway, frontend) e **schema** (migrations). Ele **não cria recursos Azure**.

---

## Mapa das fases

```
─── Tudo à mão no Portal / Entra ───────────────────────────────
Fase 1  Tenant CIAM (External ID) + user flow Email/OTP (+Google opcional)
Fase 2  App Reg SPA cliente   (CIAM)        ─┐  daqui saem os
Fase 3  App Reg admin (workforce) + App Role ─┘  4 GUIDs reais Jwt__*
Fase 4  Infra do gateway: ACR + Environment + Container App + App Settings
─── Único passo automatizado (precisa do fork) ─────────────────
Fase 5  Fork + Sync + Actions:  acao=migrations → acao=gateway → acao=frontend
─── Validação no browser / SQL ─────────────────────────────────
Fase 6  Login cliente (CIAM) e2e
Fase 7  Login admin (workforce) + App Role
Fase 8  Migração users v1 → CIAM (SQL, aditiva — o clímax)
```

---

## Conceitos (3 ideias)

1. **Dois mundos, dois produtos, duas URLs.** O cliente compra na *bilheteria pública* (qualquer um se cadastra) = **External ID** em `<seu-tenant>.ciamlogin.com`. O funcionário entra pela *portaria de serviço* com o crachá da empresa = **workforce** em `login.microsoftonline.com`.
2. **Gateway issuer-agnóstico.** Ele valida qualquer token **por discovery** (busca a chave pública do emissor numa URL `.well-known`). Aceitar um novo mundo de identidade é **configuração** (trocar a authority), não código novo.
3. **Migração aditiva.** Ao "migrar" um usuário antigo pro CIAM, a senha bcrypt **fica intacta**; você só **adiciona** um ponteiro (`users.entra_oid`). O mesmo humano passa a ter duas credenciais independentes — provar isso é o clímax do lab.

> ⚠️ **O engano que quebra o lab:** `ciamlogin.com` **≠** `microsoftonline.com`. Login do cliente em `microsoftonline.com` → `AADSTS50011`. E **nunca** use `b2clogin.com` (Azure AD B2C é legado): este lab é **exclusivamente Entra External ID** (`ciamlogin.com`).

---

## Preencha os SEUS valores

Este lab reusa os recursos das **Oitavas (F1)** e cria os **novos** das Quartas. Cada aluno tem o próprio fork, subscription e nomes. Anote os **seus** valores aqui antes de começar — todas as fases referenciam estes placeholders.

> 💡 **Sufixo:** escolha um sufixo curto e único (ex.: suas iniciais + número) e use-o em todos os recursos novos. **ACR** só aceita **letras e números** (sem hífen).

| Recurso | Convenção sugerida | Seu valor |
|---|---|---|
| Subscription | `<sua-subscription>` | ____________________ |
| Subscription ID | (GUID) | ____________________ |
| Região | `<sua-regiao>` | ____________________ |
| Resource Group | `<seu-rg>` (reuse o das Oitavas) | ____________________ |
| SQL Server | `<seu-sql-server>` (DB `FIFA2026Tickets`) | ____________________ |
| Function App F1 | `<seu-func>` → `https://<seu-func>.azurewebsites.net` | ____________________ |
| Frontend Web App | `<seu-frontend>` → `https://<seu-frontend>.azurewebsites.net` | ____________________ |
| Backend v1 Web App | `<seu-backend>` (intocado — comparação didática) | ____________________ |
| Container Registry (ACR) | `cr<sufixo>` → `cr<sufixo>.azurecr.io` (só letras/números) | ____________________ |
| Container Apps Environment | `cae-<sufixo>` | ____________________ |
| Container App (gateway) | `ca-gateway-<sufixo>` | ____________________ |
| FQDN do gateway | `<gateway-fqdn>` (gerado pelo Azure) | ____________________ |
| Tenant CIAM (nome) | `<seu-tenant>` | ____________________ |
| Domínio inicial CIAM | `<seu-tenant>.onmicrosoft.com` | ____________________ |
| Login do cliente CIAM | `<seu-tenant>.ciamlogin.com` | ____________________ |
| Tenant ID CIAM = `Jwt__CiamTenantId` | `<CiamTenantId>` | ____________________ |
| App Reg SPA (cliente) = `Jwt__CiamClientId` | `student-<iniciais>-v2` | ____________________ |
| App Reg admin (workforce) = `Jwt__AdminClientId` | `student-<iniciais>-admin` | ____________________ |
| Tenant ID workforce = `Jwt__AdminTenantId` | `<AdminTenantId>` | ____________________ |

> 💡 Você terá **DOIS** tenants e **DOIS** client IDs: o **CIAM** (cliente, `*.ciamlogin.com`) e o **workforce** (admin). Anote os dois separadamente — é fácil confundir.

---

## Fase 1 — Tenant CIAM (Entra External ID) + user flow

Tudo aqui é no **Microsoft Entra admin center** ([entra.microsoft.com](https://entra.microsoft.com)) — **não** no `portal.azure.com`.

### 1.1 Criar o tenant External ID

A criação tem uma **bifurcação**: o Azure oferece duas formas. Leia a tabela, decida UMA e siga só os passos dela.

| Forma de criar | Quando usar | Precisa de subscription? | Tem etapa de billing (1.2)? |
|---|---|---|---|
| **A · 30-day free trial** *(recomendado)* | tenant descartável de aula; mais rápido | **Não** | **Não** (pule a 1.2) |
| **B · Use Azure Subscription** | se o trial **não aparecer** ou se quiser um tenant que não expira | **Sim** (`<sua-subscription>` + `<seu-rg>`) | **Sim** (faça a 1.2) |

Permissão: sua conta precisa do papel **Tenant Creator** na subscription (e **Contributor/Owner** para a Opção B). Se a criação falhar com erro de provider, registre uma vez: `az provider register -n Microsoft.AzureActiveDirectory`.

**Passos comuns (A e B):**

1. Acesse [entra.microsoft.com](https://entra.microsoft.com) e faça login.
2. Vá em **Entra ID → Overview → Manage tenants**.
3. Clique em **`Create`**.
4. Selecione **External** e clique em **Continue**.
5. Escolha **30-day free trial** (Opção A) ou **Use Azure Subscription** (Opção B).

**Opção A — 30-day free trial:**

6. Preencha **Tenant Name** (`<seu-tenant>`) e **Domain Name** (`<seu-tenant>` → vira `<seu-tenant>.onmicrosoft.com`).
7. Selecione a **Location**.
8. Confirme e crie (pode levar até 30 min; acompanhe no sino **Notifications**). **Pule a 1.2.**

**Opção B — Use Azure Subscription:**

6. Preencha **Tenant Name** (`<seu-tenant>`) e **Domain Name** (`<seu-tenant>`).
7. Selecione **Country/Region**.
8. Clique em **Next: Add a subscription** → escolha `<sua-subscription>` e `<seu-rg>`.
9. **Review + Create → Create** (pode levar até 30 min). Depois faça a **1.2**.

**Trocar para o tenant recém-criado (A e B):**

10. Ícone de engrenagem **Settings → Directories + subscriptions**.
11. Em **Directory name**, localize o `<seu-tenant>` e clique em **Switch**.
12. Vá em **Tenant overview → Overview** e anote o **Tenant ID** (= `Jwt__CiamTenantId` = `<CiamTenantId>`) e o **Primary domain**. O host de login do cliente é o subdomínio com `.ciamlogin.com` → `<seu-tenant>.ciamlogin.com`.

> 💡 **Location não muda depois** — escolha certo na criação.

### 1.2 Billing — só na Opção B (na prática, nada a fazer)

O vínculo é **subscription → tenant**, não tenant↔tenant. Quem criou pela **Opção B já está vinculado** (aconteceu ao escolher a subscription na criação). O aviso *"An Azure subscription is required… SLA"* é **informativo**, não é cobrança e **não bloqueia** o lab. Para apenas conferir, vá no **tenant workforce** (dono da subscription): **External Identities → Overview → Subscriptions → Linked subscriptions**.

### 1.3 Criar o user flow (sign-up/sign-in)

1. **External Identities → User flows → `New user flow`**.
2. **Name** (ex.: `SignUpSignIn`).
3. **Identity providers:** marque **Email Accounts → Email one-time passcode** (OTP).
4. **User attributes:** escolha o que coletar → **Create**.

### 1.4 Google como Identity Provider — OPCIONAL

Pule se quiser: o **Email OTP** já cobre o lab inteiro. Para login social, crie o OAuth client no **[Apêndice 2](#apêndice-2--google-oauth-opcional)** e volte com Client ID + secret. Depois: **All identity providers → Built-in → Google → Configure** (cole ID/secret → Save) e ative no **User flow → Settings → Identity providers → Google → Save**.

✅ **Checkpoint:** tenant CIAM criado e **selecionado** (canto superior mostra `<seu-tenant>`); **Tenant ID** anotado (= `<CiamTenantId>`); login do cliente em `<seu-tenant>.ciamlogin.com`; user flow Email+OTP ativo.

➡️ **Próximo:** registrar a App do **cliente** dentro deste mesmo tenant CIAM.

---

## Fase 2 — App Registration SPA (cliente) no tenant CIAM

Confirme no topo do Entra admin center que você está no tenant **CIAM** (`*.ciamlogin.com`).

1. **Entra ID → App registrations → `New registration`**.
2. **Name:** `student-<iniciais>-v2` · **Account types:** single-tenant → **Register**.
3. Na **Overview**, anote **Application (client) ID** (= `Jwt__CiamClientId` e `VITE_CIAM_CLIENT_ID`).
4. **Authentication → Add a platform → Single-page application (SPA)**.
5. Em **Redirect URIs**, adicione os **dois** abaixo (sem barra final, sem path):
   - `https://<seu-frontend>.azurewebsites.net`
   - `http://localhost:5173`
6. **NÃO** crie client secret (SPA é público, usa PKCE) → **Save**.
7. Vincule ao user flow: **External Identities → User flows →** o flow **→ Use → Applications → `Add application`** → `student-<iniciais>-v2` → **Select**.

> ⚠️ Plataforma tem de ser **Single-page application (SPA)**, **não** *Web*. O frontend faz login com `redirectUri = window.location.origin` (o mesmo SPA serve prod e local) — por isso os dois URIs exatos acima. Se escolher "Web" por engano, corrija no **Manifest**. Há um app `b2c-extensions-app` na lista — **não apague**.

✅ **Checkpoint:** App Reg SPA no CIAM; **client ID** anotado; redirect SPA com os 2 URIs; vínculo ao user flow feito.

➡️ **Próximo:** o segundo mundo — a App do **admin** no tenant workforce.

---

## Fase 3 — App Registration admin (workforce) + App Role `Admin`

Troque para o tenant **workforce** (domínio `*.onmicrosoft.com`, **não** `ciamlogin.com`).

1. **Entra ID → App registrations → `New registration`** → Name `student-<iniciais>-admin` → single-tenant → **Register**.
2. Anote **Application (client) ID** (= `Jwt__AdminClientId`) e **Directory (tenant) ID** (= `Jwt__AdminTenantId` = `<AdminTenantId>`, o **workforce**, diferente do CIAM).
3. **Authentication → Add a platform → Single-page application (SPA)** → adicione os **mesmos dois** redirect URIs (sem barra final, sem path):
   - `https://<seu-frontend>.azurewebsites.net`
   - `http://localhost:5173`
4. **App roles → `Create app role`**: Display `Admin` · Allowed member types **Users/Groups** · **Value `Admin`** · habilitada → **Apply**.
5. Atribua: **Enterprise applications →** `student-<iniciais>-admin` **→ Users and groups → `Add user/group`** → seu admin → role `Admin` → **Assign**.

> ⚠️ O admin usa o **mesmo SPA** do cliente (`redirectUri = window.location.origin`), por isso esta App Reg workforce precisa **dos mesmos** redirect URIs SPA da Fase 2. Sem eles o login do admin falha com `AADSTS50011`.

✅ **Checkpoint:** App Reg admin no workforce com redirect SPA; App Role `Admin` criada e **atribuída**. Agora você tem os **4 GUIDs reais** `Jwt__*` (CIAM tenant/client + admin tenant/client).

➡️ **Próximo:** criar a infra do gateway no Portal e plugar esses 4 GUIDs.

---

## Fase 4 — Infra do gateway no Portal (ACR + Environment + Container App)

Esta é a **única fonte** do provisionamento do gateway. Tudo à mão no **[portal.azure.com](https://portal.azure.com)** (⚠️ **não** no `entra.microsoft.com` desta vez). Confirme no topo que está na **`<sua-subscription>`** e no **`<seu-rg>`**. O GitHub Actions só entra na Fase 5 (publica a imagem).

### 4.1 Criar o Azure Container Registry (ACR)

1. Busca do topo → **Container registries** → **`+ Create`**.
2. Aba **Basics**:
   - **Subscription:** `<sua-subscription>` · **Resource group:** `<seu-rg>`.
   - **Registry name:** `cr<sufixo>` (só letras/números, único globalmente → `cr<sufixo>.azurecr.io`).
   - **Location:** `<sua-regiao>` · **SKU:** **Basic**.
3. **Review + create → Create → Go to resource**.
4. **Settings → Access keys** → ligue **Admin user** = **Enabled**.
5. Anote **Login server** (`cr<sufixo>.azurecr.io`), **Username** e uma **password**.

### 4.2 Criar o Container Apps Environment

1. Busca do topo → **Container Apps** → **`+ Create`** (abre o assistente do Container App).
2. Aba **Basics**, em **Container Apps Environment**, clique em **`Create new`**.
3. **Environment name:** `cae-<sufixo>` · **Region:** `<sua-regiao>` → **Create** (volta ao Basics já com o env selecionado).

### 4.3 Criar o Container App do gateway

Suba com **imagem placeholder pública** — a imagem real vem pelo Actions (Fase 5).

1. Aba **Basics:** **Subscription** `<sua-subscription>` · **Resource group** `<seu-rg>` · **Container app name** `ca-gateway-<sufixo>` · **Environment** `cae-<sufixo>` → **Next: Container**.
2. Aba **Container:** mantenha **Use quickstart image** marcada (ACR ainda vazio). CPU/memória no menor preset → **Next: Ingress**.
3. Aba **Ingress:** **Ingress** = **Enabled** · **Ingress traffic** = **Accepting traffic from anywhere** · **Target port** = **`8080`**.
4. **Review + create → Create → Go to resource**.
5. Na **Overview**, copie a **Application Url** — é o seu **`<gateway-fqdn>`** (vira a Variable `GATEWAY_V2_URL`).
6. **Settings → Registries → `+ Add`** → **Registry** = `cr<sufixo>.azurecr.io` → **Authentication** = **Admin Credentials** → **Save**.

> ⚠️ **Target port = 8080** é crítico: a imagem do gateway expõe a porta **8080** (`Dockerfile`: `EXPOSE 8080` + `ASPNETCORE_URLS=http://+:8080`). Qualquer outro valor = **502** em tudo.

### 4.4 App Settings de identidade (gateway é fail-closed)

O gateway **só sobe com as 4 `Jwt__*` presentes**. No Container App: **Application → Containers → `Edit and deploy`** → selecione o container → aba **Environment variables** → adicione as 6 (Source = Manual entry) → **Save → Create**:

| App Setting | Valor |
|---|---|
| `Jwt__CiamTenantId` | Tenant ID do CIAM (Fase 1.1) = `<CiamTenantId>` |
| `Jwt__CiamClientId` | Client ID da App Reg SPA (Fase 2) |
| `Jwt__AdminTenantId` | Tenant ID do workforce (Fase 3) = `<AdminTenantId>` |
| `Jwt__AdminClientId` | Client ID da App Reg admin (Fase 3) |
| `FunctionAppF1Url` | `https://<seu-func>.azurewebsites.net` (sem ela `/purchase` dá 502) |
| `Gateway__FrontendOrigin` | `https://<seu-frontend>.azurewebsites.net` (CORS restrito ao front) |

> 🔒 **Duplo underscore:** `Jwt:CiamTenantId` na config vira `Jwt__CiamTenantId` em env var (o `:` não é válido). A connection string do SQL **NÃO** vai no gateway (fica na Function). Para só testar a infra antes de ter os GUIDs reais, pode usar 4 GUIDs placeholder (válidos em forma): o gateway sobe e o `401` sem token já funciona; o fluxo com token real só fecha com os 4 GUIDs reais.

> 💡 **Alternativa CLI (Cloud Shell)** — toda a Fase 4 em um bloco (⚠️ não imprima a senha em logs compartilhados):
> ```bash
> az acr create -g <seu-rg> -n cr<sufixo> --sku Basic --admin-enabled true --location <sua-regiao> -o table
> az containerapp env create -g <seu-rg> -n cae-<sufixo> --location <sua-regiao> -o table
> az containerapp create -g <seu-rg> -n ca-gateway-<sufixo> \
>   --environment cae-<sufixo> --image mcr.microsoft.com/k8se/quickstart:latest \
>   --target-port 8080 --ingress external --min-replicas 0 --max-replicas 1 -o table
> ACR_USER=$(az acr credential show -g <seu-rg> -n cr<sufixo> --query username -o tsv)
> ACR_PASS=$(az acr credential show -g <seu-rg> -n cr<sufixo> --query "passwords[0].value" -o tsv)
> az containerapp registry set -g <seu-rg> -n ca-gateway-<sufixo> \
>   --server cr<sufixo>.azurecr.io --username "$ACR_USER" --password "$ACR_PASS" -o table
> az containerapp update -g <seu-rg> -n ca-gateway-<sufixo> --set-env-vars \
>   "Jwt__CiamTenantId=<CiamTenantId>" "Jwt__CiamClientId=<CLIENT_ID_SPA_CIAM>" \
>   "Jwt__AdminTenantId=<AdminTenantId>" "Jwt__AdminClientId=<CLIENT_ID_ADMIN>" \
>   "FunctionAppF1Url=https://<seu-func>.azurewebsites.net" \
>   "Gateway__FrontendOrigin=https://<seu-frontend>.azurewebsites.net" -o table
> ```

✅ **Checkpoint:** ACR `cr<sufixo>` (Admin Enabled), Environment `cae-<sufixo>`, Container App `ca-gateway-<sufixo>` rodando (placeholder) com **ingress externo na porta 8080**, **Application Url** anotada (= `<gateway-fqdn>` = `GATEWAY_V2_URL`), **ACR conectado** nas Registries e as **6 App Settings** presentes (4 `Jwt__*` com os GUIDs das Fases 1–3).

➡️ **Próximo:** com tudo provisionado à mão, agora — e só agora — entra o GitHub Actions.

---

## Fase 5 — Fork + Sync + Actions (o único passo automatizado)

Toda a infra acima foi criada **à mão**. Este é o **último bloco de deploy**: o Actions só **constrói e publica código** (schema + imagens). Precisa do **fork** porque é nele que ficam o workflow e os Secrets/Vars.

### 5.1 Preparar o fork

1. Você reusa o **fork das Oitavas**. Abra-o no GitHub → **Sync fork** para trazer a branch nova **`phase-04-quartas`** (e as migrations das Quartas) do upstream.
2. Se **ainda não tem fork**: forke o repo **com TODAS as branches** (na tela de fork, **desmarque** *Copy the `main` branch only*).
3. Confirme os **Secrets e Variables** do Actions conforme o **[Apêndice 1](#apêndice-1--vars-e-secrets-do-github)** (nomes exatos).

### 5.2 Rodar o workflow — nesta ordem

Sempre em **Actions → "Lab Quartas de Final" → Run workflow → branch `phase-04-quartas`**, variando o `acao`:

1. **`acao = migrations`** — aplica `phase-01`, `phase-03` e a **nova `phase-04-ciam-link.sql`** (cria `users.entra_oid` **vazia** + índice `UQ_users_entra_oid`). O workflow abre/reverte acesso temporário ao SQL privado (idempotente; pode repetir). O **preenchimento** da coluna é o hands-on da [Fase 8](#fase-8--migração-users-v1--ciam-sql--o-clímax) — de propósito **não** roda aqui.
2. **`acao = gateway`** — `dotnet build/test`, **build & push** da imagem no ACR (`cr<sufixo>.azurecr.io/gateway:<sha>`) e `az containerapp update --image` (troca o placeholder pela imagem real) + smoke. Re-rode só quando trocar o código.
3. **`acao = frontend`** — antes, garanta **SCM Basic Auth `On`** no Web App do frontend e capture o `AZURE_FRONTEND_PUBLISH_PROFILE` **depois** disso; configure `VITE_CIAM_AUTHORITY` e `VITE_CIAM_CLIENT_ID`. O job faz `npm ci` + `vite build` (com `VITE_CIAM_*`) + deploy.

### 5.3 Smoke do gateway

```bash
FQDN="<gateway-fqdn>"
sleep 20   # cold start: min-replicas=0
curl -fsS "https://${FQDN}/health"
# → {"status":"healthy","service":"gateway-yarp"}   (rota anônima)
curl -s -o /dev/null -w '%{http_code}\n' -X POST "https://${FQDN}/purchase" \
  -H "Content-Type: application/json" -d '{"matchId":1,"category":"VIP","userId":1,"quantity":1}'
# → 401   (fail-closed: sem token o gateway recusa)
```

> ⚠️ Authority do frontend em `login.microsoftonline.com` → `AADSTS50011`. Como `ciamlogin.com` é authority "non-AAD", o MSAL exige `knownAuthorities: ['<seu-tenant>.ciamlogin.com']` (o `authV2.ts` já contempla). `VITE_CIAM_AUTHORITY` aponta para `https://<seu-tenant>.ciamlogin.com/`.

✅ **Checkpoint:** três jobs verdes; `/health` = 200; `POST /purchase` sem token = **401**; revisão ativa do Container App aponta para `cr<sufixo>.azurecr.io/gateway:<sha>` (não mais o placeholder); frontend publicado com a authority CIAM embutida.

➡️ **Próximo:** validar o login real no browser.

---

## Fase 6 — Login do cliente (CIAM) e2e

1. Abra o frontend → **Entrar (v2)** → redireciona para `<seu-tenant>.ciamlogin.com`.
2. **Sign-up self-service:** "Continuar com Google" **ou** email + **OTP**.
3. Faça uma compra (`POST /purchase`) — o SPA envia `Authorization: Bearer <token-CIAM>`.
4. Confirme no SQL:
   ```sql
   SELECT TOP 5 id, user_id, entra_oid, status, created_at
   FROM dbo.purchases WHERE entra_oid IS NOT NULL ORDER BY id DESC;
   ```

> ⚠️ **GATE — confirme em runtime:** cole o access token em [jwt.ms](https://jwt.ms) e verifique o **formato exato** do `iss` (termina em `…/v2.0`), o `aud` (= seu client ID) e o claim `oid`. É aqui que você trava o formato do issuer CIAM e o `knownAuthorities`. Nunca cole tokens de produção — em sala é trial descartável.

✅ **Checkpoint (AC-11):** login CIAM → gateway valida → `X-Entra-OID` propagado → `purchases.entra_oid` (origem CIAM) gravado ao lado de registros v1.

➡️ **Próximo:** o segundo mundo — login do admin.

> ☕ **Ponto de pausa natural** — se dividir em dois encontros, encerre aqui (cliente CIAM real validado pelo gateway = uma aula completa).

---

## Fase 7 — Login do admin (workforce) + App Role

Pré-condição: Fase 3 feita e os `Jwt__Admin*` reais já no gateway (Fase 4.4).

1. Logue como admin (authority `https://login.microsoftonline.com/<AdminTenantId>`). Em [jwt.ms](https://jwt.ms): `iss = login.microsoftonline.com/.../v2.0` e `roles: ["Admin"]`.
2. Teste a separação: um **cliente CIAM válido** numa rota admin recebe **403** (autenticado, sem a role) — não 401.

✅ **Checkpoint (AC-13):** login admin via workforce com `roles:["Admin"]`, separado do cliente CIAM. Dois mundos coexistindo, validados pela **mesma** mecânica issuer-agnóstica.

➡️ **Próximo:** o clímax — migrar usuários v1 para o CIAM sem apagar nada.

---

## Fase 8 — Migração `users` v1 → CIAM (SQL — o clímax)

A coluna `users.entra_oid` já existe (Fase 5.2) **vazia** — você a **preenche** aqui, de forma aditiva.

**8.1 Listar os alvos**
```sql
SELECT id, name, email, entra_oid FROM dbo.users WHERE entra_oid IS NULL ORDER BY id;
```

**8.2 Sign-up no CIAM com o MESMO email do v1** — para cada conta, faça sign-up self-service no CIAM (Fase 6) com **o email idêntico** ao de `users`. A senha **bcrypt NÃO vai** pro CIAM; o `users.password` fica **intacto** no caminho v1.

**8.3 Capturar o `oid` emitido pelo CIAM** — via app (token em jwt.ms/DevTools) **ou** via Portal (Entra admin center → tenant CIAM → **Users** → o usuário → **Object ID**).

**8.4 Vincular o `oid` ao registro v1 (idempotente)**
```sql
UPDATE dbo.users
SET    entra_oid = @oid       -- oid do 8.3
WHERE  email = @email         -- MESMO email do v1
  AND  entra_oid IS NULL;     -- guard de idempotência
```
Idempotência: `WHERE entra_oid IS NULL` (2ª execução = 0 linhas) + índice UNIQUE filtrado `UQ_users_entra_oid` + sign-up nativo do CIAM (email já existente não duplica).

**8.5 Provar a coexistência (o clímax)**
```sql
SELECT u.id AS user_id_v1, u.email,
       CASE WHEN u.password LIKE '$2%' THEN 'bcrypt-presente' ELSE 'sem-bcrypt' END AS credencial_v1,
       u.entra_oid AS oid_ciam_v2,
       CASE WHEN u.password IS NOT NULL AND u.entra_oid IS NOT NULL
            THEN 'COEXISTE (v1 bcrypt + v2 CIAM)'
            WHEN u.entra_oid IS NULL THEN 'so v1 (nao migrou)'
            ELSE 'estado inesperado' END AS status_migracao
FROM dbo.users u WHERE u.email = @email;
```
Esperado: `status_migracao = COEXISTE (v1 bcrypt + v2 CIAM)`.

> 💡 **Rollback (aditivo ⇒ trivial):** desfazer um vínculo = `UPDATE dbo.users SET entra_oid = NULL WHERE email = @email;`. Reverter a migration = `DROP INDEX UQ_users_entra_oid ON dbo.users; ALTER TABLE dbo.users DROP COLUMN entra_oid;`. **Nunca** crie backup table no SQL (regra do projeto).

✅ **Checkpoint (AC-16):** uma linha de `users` com as **duas identidades** vivas lado a lado. Modernização sem destruição, provada em banco.

---

## Apêndice 1 — Vars e Secrets do GitHub

Fork → **Settings → Secrets and variables → Actions**. Os **nomes** abaixo são **fixos** (iguais para todos); os **valores** são os **seus** (placeholders da [tabela do topo](#preencha-os-seus-valores)).

### GitHub Variables

| Nome EXATO | Valor (seu) | Usada em (ação) |
|---|---|---|
| `SQL_SERVER` | `<seu-sql-server>` | migrations |
| `RESOURCE_GROUP` | `<seu-rg>` | migrations |
| `ACR_LOGIN_SERVER` | `cr<sufixo>.azurecr.io` | gateway |
| `PHASE04_CONTAINERAPP_NAME` | `ca-gateway-<sufixo>` | gateway |
| `PHASE04_RESOURCE_GROUP` | `<seu-rg>` | gateway |
| `FRONTEND_APP_NAME` | `<seu-frontend>` | frontend |
| `GATEWAY_V2_URL` | `https://<gateway-fqdn>` | frontend (→ `VITE_GATEWAY_V2_URL`) |
| `BACKEND_URL` | `https://<seu-backend>.azurewebsites.net` | frontend |
| `FUNCTION_V2_URL` | `https://<seu-func>.azurewebsites.net` | frontend (→ `VITE_FUNCTION_V2_URL`) |
| `VITE_CIAM_AUTHORITY` | `https://<seu-tenant>.ciamlogin.com/` | frontend |
| `VITE_CIAM_CLIENT_ID` | Client ID da App Reg SPA (Fase 2) | frontend |

> ⚠️ As vars do gateway têm prefixo **`PHASE04_`** (`PHASE04_CONTAINERAPP_NAME`, `PHASE04_RESOURCE_GROUP`) — é exatamente o que o YAML lê; configure assim ou o workflow não encontra. `SQL_SERVER`/`RESOURCE_GROUP` têm fallback para defaults internos do YAML, mas mantê-las explícitas é mais claro.

### GitHub Secrets

| Nome EXATO | Conteúdo | Usada em (ação) |
|---|---|---|
| `AZURE_CREDENTIALS` | JSON do Service Principal com acesso ao RG | migrations + gateway |
| `SQL_CONNECTION_STRING` | connection string ADO.NET do `FIFA2026Tickets` | migrations |
| `AZURE_FRONTEND_PUBLISH_PROFILE` | publish profile do `<seu-frontend>` | frontend |

> **Montar a `SQL_CONNECTION_STRING` (Cloud Shell PowerShell):**
> ```powershell
> $server = "<seu-sql-server>"; $senha = "<senha-adminsql>"
> "Server=$server.database.windows.net,1433;Database=FIFA2026Tickets;User Id=adminsql;Password=$senha;Encrypt=true;TrustServerCertificate=true"
> ```

> 📌 **Sobras da F1 (Oitavas) — NÃO confunda:** existem no fork mas o workflow das Quartas **NÃO lê**. Deixe-as quietas.
> - Variables inertes: `BACKEND_APP_NAME`, `FUNCTION_APP_NAME`
> - Secrets inertes: `AZURE_BACKEND_PUBLISH_PROFILE`, `FUNCTION_PUBLISH_PROFILE`

---

## Apêndice 2 — Google OAuth (opcional)

> 🟢 Só faça se for oferecer login social do Google (o **Email OTP** da Fase 1.3 já cobre o lab). Faça **depois** de ter o **Tenant ID** do CIAM (Fase 1.1) — os redirect URIs dependem dele.

A interface atual chama-se **Google Auth Platform** (rótulos legados *APIs & services → OAuth consent screen* entre parênteses).

1. [console.cloud.google.com](https://console.cloud.google.com) (conta do lab) → **New Project** (ex.: `fifa2026-ciam-lab`) → **Create** → selecione-o.
2. **☰ → Google Auth Platform → Branding**. No wizard **Get started**: App name + User support email (Gmail do lab); **Audience = External**; contato → Save.
3. **Audience:** confirme **Publishing status = Testing** e adicione o Gmail do lab em **Test users**.
4. **Branding → Authorized domains:** adicione **`ciamlogin.com`** e **`microsoftonline.com`**.
5. **Clients → Create client → Web application** (ex.: `entra-ciam-callback`) → em **Authorized redirect URIs** cole **os 7** abaixo, trocando `<tenant-ID>` pelo seu `<CiamTenantId>` e `<tenant-subdomain>` pelo seu `<seu-tenant>`:

```text
https://login.microsoftonline.com
https://login.microsoftonline.com/te/<tenant-ID>/oauth2/authresp
https://login.microsoftonline.com/te/<tenant-subdomain>.onmicrosoft.com/oauth2/authresp
https://<tenant-ID>.ciamlogin.com/<tenant-ID>/federation/oidc/accounts.google.com
https://<tenant-ID>.ciamlogin.com/<tenant-subdomain>.onmicrosoft.com/federation/oidc/accounts.google.com
https://<tenant-subdomain>.ciamlogin.com/<tenant-ID>/federation/oauth2
https://<tenant-subdomain>.ciamlogin.com/<tenant-subdomain>.onmicrosoft.com/federation/oauth2
```

> Cadastre os 7 ou o login falha com `redirect_uri_mismatch`. Link: https://learn.microsoft.com/en-us/entra/external-id/customers/how-to-google-federation-customers

6. **Create** → copie **Client ID** e **Client secret** (o secret só aparece agora por inteiro) → leve para a **Fase 1.4**.

---

## Apêndice 3 — Gemini key (adiantamento p/ o último lab)

Adiantamento para o **último lab** (chatbot LLM). Como você cria a conta Google de qualquer jeito (a Fase 1.4 usa o Google como IdP opcional), já deixe a key pronta.

1. Crie/abra uma conta **Gmail exclusiva do lab** (ex.: `fifa2026.lab.<iniciais>@gmail.com`), em janela anônima.
2. Acesse **https://aistudio.google.com/apikey** logado nessa conta → aceite os termos.
3. **Create API key → Create API key in new project** → copie e guarde como `GEMINI_API_KEY` (nunca no código). Modelo do lab: `gemini-2.5-flash`.

---

## Apêndice 4 — Troubleshooting

| Sintoma | Causa provável | Mitigação |
|---|---|---|
| **502** em toda chamada | targetPort do ingress ≠ **8080** | ingress targetPort = **8080** (Fase 4.3) |
| **502** só em `/purchase` | `FunctionAppF1Url` ausente/errada | apontar p/ `https://<seu-func>.azurewebsites.net` (Fase 4.4) |
| Container App `Failed`/CrashLoop | `Jwt__*` ausente/vazia/`"common"` (fail-closed) | 4 `Jwt__*` presentes; placeholder serve p/ subir e fazer o 401 |
| `/purchase` dá **200** sem token | gateway não fail-closed (config errada) | revisar `AddJwtBearer`/`Jwt__*`; deveria ser **401** |
| `/health` não responde no 1º hit | cold start (`min-replicas=0`) | aguardar ~20s e repetir |
| `AADSTS50011` no login do cliente | authority com `microsoftonline.com` | `VITE_CIAM_AUTHORITY` = `<seu-tenant>.ciamlogin.com` (Fase 5.2) |
| `AADSTS50011` no login do admin | redirect URI faltando na App Reg workforce | adicionar `https://<seu-frontend>.azurewebsites.net` + `http://localhost:5173` como SPA (Fase 3) |
| MSAL recusa authority "não confiável" | falta `knownAuthorities` | `knownAuthorities: ['<seu-tenant>.ciamlogin.com']` (já no `authV2.ts`) |
| **401 "Invalid issuer"** (cliente/admin) | `Jwt__*` placeholder/errado | trocar pelos 4 GUIDs reais (Fase 4.4) |
| `roles` ausente no token admin | role não atribuída | Enterprise applications → atribuir `Admin` (Fase 3) |
| Cliente CIAM em rota admin dá 401 (esperava 403) | policy fixando esquema | `AdminOnly` só `RequireRole("Admin")` (já no código) |
| `redirect_uri_mismatch` (Google) | redirect URI ≠ callback do Entra | cadastrar **todos** os 7 URIs (Apêndice 2) |
| Vars do gateway "não encontradas" | esqueceu o prefixo `PHASE04_` | usar `PHASE04_CONTAINERAPP_NAME` / `PHASE04_RESOURCE_GROUP` |
| Migrations falham por firewall | SQL privado; runner sem regra | o workflow abre/reverte acesso temporário (já tratado no YAML) |
| Branch `phase-04-quartas` não aparece no fork | fork desatualizado | **Sync fork** com o upstream (Fase 5.1) |
| Usuário não migra / `so v1` | UPDATE não rodou / email divergente | re-executar UPDATE idempotente (Fase 8.4) |
| Só "Use Azure Subscription" (sem trial) | trial 30d quase nunca é ofertado | seguir por **Use Azure Subscription** — free 50K MAU, não expira (Fase 1.1) |
| Aviso *"Azure subscription is required… SLA"* | aviso informativo, **não é cobrança** | quem criou via **Use Azure Subscription** já está vinculado; nada a fazer (Fase 1.2) |
| "Insufficient privileges" ao criar o tenant | conta sem Owner / RP não registrado | conta **Global Admin + Owner** + `az provider register -n Microsoft.AzureActiveDirectory` |
