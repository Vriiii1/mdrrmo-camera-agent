# Trust Boundary — Local UI

The local UI on `127.0.0.1:8787` is **unauthenticated** by design. Loopback bind prevents LAN access.

**Same-host actors with Windows admin rights** (any process running as Administrator on the LGU's Windows box,
including LocalSystem services, RDP-forwarded sessions, or a misconfigured Docker container with host
networking) are implicitly trusted to call `POST /cameras` and similar endpoints.

This matches the operational reality of Philippine LGU IT — the install requires Administrator anyway,
and LGU IT's Windows admin account is the trust boundary.

Phase-2 hardening options (tracked separately):
- Windows Negotiate auth (bind trust to a specific Windows admin SID)
- DPAPI-stored pairing code (resist drive-by admin actors)
