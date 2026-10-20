# Security policy

## Reporting a vulnerability

Please do not open a public issue for a security problem. Use GitHub's private reporting instead: **Security > Report a vulnerability** on the [repository page](https://github.com/eupsa/client-ssh/security/advisories/new). Include the steps to reproduce and the version. You will get an answer as soon as the maintainer can, usually within a week.

## Scope

ClientSSH runs the Windows OpenSSH client (`ssh.exe`) and keeps hosts and settings in `%LOCALAPPDATA%\ClientSSH`. It stores no passwords or keys itself. Of interest: command-line injection through host or user fields, unsafe handling of the update repository or links, and anything that makes data leave the PC without the user asking.

## Supported versions

Only the latest release gets fixes.
