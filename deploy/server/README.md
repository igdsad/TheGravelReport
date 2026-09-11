# GravelReview server deployment

`IncidentReview.Host.Server` is the always-on, Linux-native composition root for
the custom-event HTTP receiver. It uses the same versioned wire contract and
first-write-wins SQLite inbox as the desktop application. The published output
is self-contained; the target machine does not need a system-wide .NET runtime.

## Publish on the build machine

From the repository root:

```powershell
.\eng\publish-server.ps1
```

The validated archive and its SHA-256 sidecar are written under
`artifacts/release/linux-x64`.

## Install on Ubuntu 24.04

The commands below assume the release archive, its separately recorded SHA-256,
and the two files in `deploy/systemd` have been copied to the server. Verify the
recorded digest before changing the live service.

```sh
sudo useradd --system --user-group --home-dir /var/lib/gravelreview \
  --shell /usr/sbin/nologin gravelreview 2>/dev/null || true
sha256sum -c GravelReview-server-linux-x64-v0.0.3-alpha.tar.gz.sha256
sudo install -d -o root -g root -m 0755 \
  /opt/gravelreview-server/releases/v0.0.3-alpha
sudo tar -xzf GravelReview-server-linux-x64-v0.0.3-alpha.tar.gz \
  -C /opt/gravelreview-server/releases/v0.0.3-alpha
sudo chown -R root:root /opt/gravelreview-server/releases/v0.0.3-alpha
sudo chmod 0755 \
  /opt/gravelreview-server/releases/v0.0.3-alpha/IncidentReview.Host.Server
sudo install -o root -g root -m 0644 gravelreview-server.service \
  /etc/systemd/system/gravelreview-server.service
sudo install -o root -g root -m 0644 gravelreview-server.env.example \
  /etc/default/gravelreview-server
sudo ln -sfnT /opt/gravelreview-server/releases/v0.0.3-alpha \
  /opt/gravelreview-server/current.next
sudo mv -Tf /opt/gravelreview-server/current.next \
  /opt/gravelreview-server/current
sudo systemctl daemon-reload
sudo systemctl enable gravelreview-server
sudo systemctl restart gravelreview-server
```

Verify readiness and inspect service logs:

```sh
curl --fail --show-error http://127.0.0.1:5088/healthz
sudo systemctl status gravelreview-server --no-pager
sudo journalctl --unit gravelreview-server --since today
```

Keep the preceding release directory until the new service has been verified.
Rollback is an atomic repoint of `current` to that directory followed by
`systemctl restart gravelreview-server`.

The service accepts `--listen-url` and `--database-path` arguments. The matching
environment variables are `INCIDENTREVIEW_SERVER_LISTEN_URL` and
`INCIDENTREVIEW_SERVER_DATABASE_PATH`; command-line values take precedence.

The protocol currently has no authentication or encryption. Expose port 5088
only to the trusted league network, or place the service behind an authenticated
TLS reverse proxy before allowing public-Internet traffic.
