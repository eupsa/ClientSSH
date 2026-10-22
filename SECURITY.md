# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub's private reporting instead: **Security > Report a vulnerability** on the [repository page](https://github.com/eupsa/client-ssh/security/advisories/new). Include the steps to reproduce and the version. You will get an answer as soon as the maintainer can, usually within a week.

## Scope

ClientSSH runs the Windows OpenSSH client (`ssh.exe`) and keeps hosts and settings in `%LOCALAPPDATA%\ClientSSH`. It stores no passwords or keys itself. Of interest: command-line injection through host or user fields, unsafe handling of the update repository or links, and anything that makes data leave the PC without the user asking.

Also in scope: the remote access protocol (`Services/Remote`). It is our own design on standard primitives (an ECDSA P-256 key per installation behind the PC number, ECDH P-256, HKDF-SHA256, AES-256-GCM, a replay window, a six-digit code to compare out loud; PCs are found through the public BitTorrent DHT) and has had no outside review, so a careful read is very welcome: anything that lets another PC see or control a screen without the person's consent, weakens the key agreement, or leaks data.

## Supported versions

Only the latest release gets fixes.
