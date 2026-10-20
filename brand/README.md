# ClientSSH · assets de marca (v2)

Abra `index.html` para navegar por tudo (funciona offline, player Lottie embutido).
Cores: só azul-escuro, preto e branco.

## Símbolo
Um "C" aberto com o prompt `>_` dentro. O "C" é o cliente, o `>_` é o shell, e a abertura do anel aponta para o servidor.

## Pastas
| Pasta | Conteúdo |
|---|---|
| `logo/` | SVGs fonte: tile azul, branco e preto, tile "small" (16–32 px), símbolo azul/preto/branco/mono (`currentColor`), wordmarks, lockups empilhados, social preview, favicon. |
| `png/` | Ícone 16–1024 px nas três variantes, wordmarks @1x/@2x, `social-preview-1280x640.png`. |
| `msix/` | Pronto para `Assets/` do projeto WinUI: Square44x44 (escalas 100–400 + targetsize 16–256, unplated e lightunplated), Square71/150/310, Wide310x150, StoreLogo, SplashScreen, LockScreenLogo. |
| `ico/` | `ClientSSH.ico` (16–256 px) e `favicon.ico`. |
| `anim/` | 20 animações Lottie (240×240, 60 fps), versão clara e `.dark.json`. `_meta.json` diz quais rodam em loop. |
| `_gerador/` | Scripts Python que geram tudo. |

## Animações
logo-reveal, logo-hover, loading-logo, loading-ring, loading-dots, connecting, authenticating, host-key-verified, host-key-warning, connected, error, reconnecting, disconnected, offline, upload, download, saving, copied, terminal-cursor, running-command.

## Paleta
Azul-escuro `#1E3A8A` · Azul-noite `#0F1F57` · Azul do topo `#2747A6` · Preto `#05070D` · Branco `#FFFFFF`.
Fonte do nome: Inter SemiBold + Bold (OFL), em curvas.

## Manifesto
Splash: `BackgroundColor="#0F1F57"`. Trechos de `Package.appxmanifest` e XAML do `AnimatedVisualPlayer` estão na seção "Uso" do `index.html`.

## Regerar
```
pip install cairosvg fonttools pillow
python3 _gerador/build.py <saida>
python3 _gerador/build_html.py <saida>   # precisa de lottie-web 5.12.2 em ../vendor/
```
