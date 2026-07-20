---
title: NzbDav
hide:
  - navigation
  - toc
---

<div class="nzbdav-hero" markdown>

![NzbDav admin UI](https://github.com/user-attachments/assets/37649514-bca5-471f-9556-0d34de2bd8e5)

# NzbDav

<p class="nzbdav-tagline" markdown>
**Mount NZBs as a virtual filesystem and stream directly from Usenet** — without downloading full media files first.
</p>

<div class="nzbdav-cta" markdown>

[Get started](getting-started/index.md){ .md-button .md-button--primary }
[View on GitHub](https://github.com/nzbdav/nzbdav){ .md-button }
[Docker image](https://github.com/nzbdav/nzbdav/pkgs/container/nzbdav){ .md-button }

</div>

<div class="nzbdav-badges" markdown>

[![Latest release](https://img.shields.io/github/v/release/nzbdav/nzbdav)](https://github.com/nzbdav/nzbdav/releases)
[![Docker](https://img.shields.io/badge/ghcr.io-nzbdav%2Fnzbdav-0d9488?logo=docker&logoColor=white)](https://github.com/nzbdav/nzbdav/pkgs/container/nzbdav)
[![CI](https://img.shields.io/github/actions/workflow/status/nzbdav/nzbdav/ci.yml?branch=main&label=CI)](https://github.com/nzbdav/nzbdav/actions)
[![License](https://img.shields.io/github/license/nzbdav/nzbdav)](https://github.com/nzbdav/nzbdav/blob/main/LICENSE)

</div>

</div>

NzbDav is a **WebDAV server** that mounts NZB documents as a browsable virtual filesystem. Content streams on demand from your Usenet provider. A **SABnzbd-compatible API** lets Sonarr, Radarr, and similar tools use it as a drop-in download client — so you can build an effectively infinite media library without storing full files on disk.

```mermaid
flowchart LR
  Arr[Sonarr_Radarr] -->|SAB_API| Queue[NzbDav_Queue]
  Queue --> Mount[WebDAV_mount]
  Mount --> Stream[Usenet_stream]
  Player[Plex_Jellyfin_rclone] --> Mount
```

## Why NzbDav

<div class="grid cards" markdown>

-   :material-folder-network:{ .lg .middle } __Virtual filesystem__

    ---

    Browse NZB contents instantly over WebDAV. No full download before you open a file.

    [:octicons-arrow-right-24: WebDAV filesystem](features/webdav-filesystem.md)

-   :material-play-circle:{ .lg .middle } __Stream and seek__

    ---

    Jump anywhere in a video. Optional NNTP pipelining for higher throughput and faster seeks.

    [:octicons-arrow-right-24: Streaming](features/streaming-seeking.md)

-   :material-download-network:{ .lg .middle } __*Arr compatible__

    ---

    SABnzbd API surface for Sonarr and Radarr, with symlink or STRM import strategies.

    [:octicons-arrow-right-24: Infinite library](use-cases/infinite-library-arr.md)

-   :material-shield-check:{ .lg .middle } __Multi-provider__

    ---

    Cascade routing, circuit breakers, data caps, and automatic failover across providers.

    [:octicons-arrow-right-24: Multi-provider](features/multi-provider.md)

</div>

## Quick start

=== "Docker run"

    ```bash
    docker run --rm -it -p 3000:3000 ghcr.io/nzbdav/nzbdav:latest
    ```

    Ephemeral trial — settings are discarded when the container exits.

=== "Docker Compose"

    ```yaml
    services:
      nzbdav:
        image: ghcr.io/nzbdav/nzbdav:latest
        container_name: nzbdav
        restart: unless-stopped
        ports:
          - "3000:3000"
        environment:
          PUID: "1000"
          PGID: "1000"
          TZ: Etc/UTC
        volumes:
          - ./config:/config
    ```

Then open `http://localhost:3000`, create your admin account, and configure a Usenet provider under **Settings**.

!!! warning "Expose carefully"

    Port `3000` is plain HTTP. Put NzbDav behind HTTPS for remote access. WebDAV uses Basic auth, so TLS matters. Prefer binding `127.0.0.1:3000:3000` when a reverse proxy runs on the host.

[Full Docker guide](getting-started/docker.md){ .md-button .md-button--primary }
[First-run checklist](getting-started/first-run.md){ .md-button }

## Documentation

<div class="grid cards" markdown>

-   :material-rocket-launch:{ .lg .middle } __Getting started__

    ---

    Install with Docker, complete first-run setup, connect Radarr and Sonarr.

    [:octicons-arrow-right-24: Start here](getting-started/index.md)

-   :material-book-open-page-variant:{ .lg .middle } __Guides__

    ---

    Architecture, import strategies, rclone mounts, media servers, Stremio, troubleshooting.

    [:octicons-arrow-right-24: Browse guides](guides/architecture.md)

-   :material-cog:{ .lg .middle } __Configuration__

    ---

    What every Settings control does, plus the headless environment-variable schema.

    [:octicons-arrow-right-24: Settings reference](configuration/index.md)

-   :material-lightbulb:{ .lg .middle } __Use cases__

    ---

    Infinite library with *Arr, streaming-only setups, multi-provider failover.

    [:octicons-arrow-right-24: Use cases](use-cases/index.md)

</div>

## Track progress on GitHub

NzbDav is developed in the open. Use GitHub to follow releases, file issues, and review pull requests.

[Repository](https://github.com/nzbdav/nzbdav){ .md-button .md-button--primary }
[Releases](https://github.com/nzbdav/nzbdav/releases){ .md-button }
[Issues](https://github.com/nzbdav/nzbdav/issues){ .md-button }
[Community hub](community/github.md){ .md-button }

## Ecosystem

NzbDav owns the streaming stack end to end. Complementary managed libraries — **UsenetSharp**, **RapidYencSharp**, **rapidyenc**, and **SharpCompress** — land connection, decode, and archive fixes in the right layer so playback improvements ship with the product.

[About the project](community/about.md){ .md-button }

## License

NzbDav is released under the [MIT License](https://github.com/nzbdav/nzbdav/blob/main/LICENSE).

!!! note "Intended use"

    NzbDav is intended for use with legally obtained content only. The maintainers do not condone piracy and will not support copyright infringement.
