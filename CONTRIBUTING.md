# Contributing

## Set up your system

The project consists of two sub projects: frontend and backend
Both share some necessary environment variables.

**Ensure that frontend and backend share the same environment configuration!**

Environment variables:

```bash
export CONFIG_PATH=/where/to/create/database/
export FRONTEND_BACKEND_API_KEY=$(head -c 32 /dev/urandom | hexdump -ve '1/1 "%.2x"')
export BACKEND_URL=http://localhost:5000
```

You need some packages in order to run the project:

- dotnet-sdk
- aspnet-runtime
- nodejs
- npm

Example installation for Arch based systems:

```bash
sudo pacman -S dotnet-sdk aspnet-runtime nodejs npm
```

## Build / run backend

### UsenetSharp (GitHub Packages)

The `UsenetSharp` dependency is resolved from GitHub Packages. Before building the backend, authenticate once (credentials are stored in your user-level NuGet config, not in the repo):

```bash
dotnet nuget add source https://nuget.pkg.github.com/nzbdav/index.json \
  --name github \
  --username nzbdav \
  --password <GITHUB_PAT_WITH_read:packages> \
  --store-password-in-clear-text
```

If the `github` source already exists locally, use `dotnet nuget update source github` with the same options instead.

### Dependabot (NuGet updates)

Dependabot cannot use `GITHUB_TOKEN` for private GitHub Packages feeds. Add a **Dependabot** secret (Settings → Secrets and variables → Dependabot):

| Secret | Value |
|--------|-------|
| `GH_PACKAGES_READ_TOKEN` | Classic PAT with `read:packages` scope |

Also grant `nzbdav/nzbdav` **Read** on the UsenetSharp package (Package settings → Manage Actions access).

```bash
cd backend

# Build (release)
dotnet publish -c Release -o ./publish

# Create database
mkdir -p $CONFIG_PATH
./publish/NzbWebDAV --db-migration

# Run backend
./publish/NzbWebDAV
```

## Build / serve frontend

Requires **Node.js 24+** (see `engines` in `frontend/package.json`).

`package.json` includes an `overrides` entry that pins `http-proxy-middleware` to the `http-proxy-node16` fork for Node compatibility. Remove it only after verifying proxy behavior against upstream `http-proxy`.

```bash
cd frontend

# Install dependencies
npm install

# Run / serve frontend with hot module replacement
npm run dev
```

## Build Docker image

### Using Docker CLI

In the root directory, pass a GitHub token so the backend build can restore `UsenetSharp` from GitHub Packages:

```bash
export GITHUB_TOKEN=<GITHUB_PAT_WITH_read:packages>
docker build --secret id=github_token,env=GITHUB_TOKEN .
```

You can also tag the release, which can be used with `docker compose`:

```bash
docker build --secret id=github_token,env=GITHUB_TOKEN -t example/nzbdav:test_build .
```

Run the container:

```bash
docker run --rm -it \
  -v /path/to/nzbdav/config:/config \
  -e PUID=1000 \
  -e PGID=1000 \
  -p 3333:3000 \
  example/nzbdav:test_build
```

### Using Docker Compose

```yaml
services:
  nzbdav:
    build: .
    ports:
      - 3333:3000
    volumes:
      - /path/to/nzbdav/config:/config
      - /path/to/nzbdav/data:/data
    environment:
      - PUID=1000
      - PGID=1000
```

Build and run container:

```bash
docker compose up
```

## Contributing

You might check types before creating a PR:

```bash
cd frontend
npm run typecheck
```
