# ClientSSH — contexto do projeto

Cliente SSH open source para Windows (repo público no GitHub, licença MIT).

## Objetivo e privacidade
- Conexão fácil e rápida.
- Hosts, usuários, chaves e senhas ficam só no PC do usuário e nunca saem dele. Sem contas, nuvem ou telemetria.
- Não gravar histórico de hosts por padrão. Qualquer armazenamento (ex.: favoritos) é opcional, local e opt-in.
- Repo público: nunca commitar chaves, senhas, known_hosts nem hosts reais de teste.

## Arquitetura
**MVVM** (Model–View–ViewModel):
- Views em XAML, sem lógica de negócio no code-behind.
- ViewModels com estado e comandos.
- Models/serviços para SSH, armazenamento local e configurações.

## Stack de UI (definida pelo usuário)
- WinUI 3
- Windows App SDK
- XAML e controles WinUI
- Fluent Design
- DirectWrite (texto)
- Direct2D (desenho 2D)
- Windows Imaging Component (WIC), para imagens
- Animações e estados: Windows Animation Manager ou animações próprias, para movimento e transições

## Referência: ProtonVPN para Windows
Fonte: github.com/ProtonVPN/win-app (código aberto), `src/Client/ProtonVPN.Client/ProtonVPN.Client.csproj`.

- **UI:** WinUI 3 (`UseWinUI=true`) com Windows App SDK self-contained. Alvo `net8.0-windows10.0.22621.0`.
- **Arquitetura:** MVVM com `CommunityToolkit.Mvvm`. Projetos separados: `Client`, `Client.Core`, `Client.Contracts`, `Client.Logic`.
- **Injeção de dependência:** Autofac.
- **Controles e utilidades:** CommunityToolkit.WinUI (SettingsControls, Controls, Converters, Triggers, Collections), `WinUIEx`, `H.NotifyIcon.WinUI` (ícone na bandeja), `Microsoft.Xaml.Behaviors.WinUI.Managed`, `MarkdownTextBlock`.
- **Serialização:** System.Text.Json e Newtonsoft.Json.
- **Backend:** Windows service separado (privilégios), comunicação entre processos, drivers e filtros C++ nativos (firewall, rede). Não se aplica a um cliente SSH.
- **Observação:** o README do repo ainda cita WPF, mas está desatualizado. O código atual do cliente é WinUI 3.

## Decisões
- **Linguagem:** C# / .NET 8 (`net8.0-windows10.0.19041.0`), Windows App SDK 2.5.1. Projeto recriado em C# a partir do template "Aplicativo WinUI em Branco (Empacotado)". O C++/WinRT inicial foi descartado.
- **MVVM:** `CommunityToolkit.Mvvm` (a instalar). DI: `Microsoft.Extensions.DependencyInjection` (a instalar), mais simples que o Autofac do ProtonVPN.
- **SSH:** motor ainda em decisão: SSH.NET ou o `ssh.exe` do Windows via ConPTY (ver "Decisões do usuário").
- O ProtonVPN não usa DirectWrite, Direct2D, WIC nem Windows Animation Manager diretamente. O WinUI 3 já os usa por baixo. Chamá-los de forma explícita só vale em casos específicos (renderização de terminal, imagens). Para animações, prefira a Composition API do WinUI.

## Decisões do usuário
- **Terminal:** próprio, com parser VT/ANSI e desenho com Direct2D e DirectWrite.
- **Dados:** não há API nem servidor. Tudo roda no PC. O app salva localmente os hosts que o usuário adicionar, em SQLite (`%LOCALAPPDATA%\ClientSSH\clientssh.db`; `CLIENTSSH_DATA_DIR` troca a pasta, e deve ser usada em testes automatizados para não mexer nos dados do usuário). O `hosts.json` antigo é importado uma vez e renomeado para `.migrated`. Importar os hosts do Windows (ex.: `~/.ssh/config`) fica para o futuro.
- **Autenticação:** todas as formas que o SSH do Windows suporta (senha, chave, keyboard-interactive, ssh-agent, FIDO2/chave de segurança, GSSAPI).
- **Motor SSH:** `ssh.exe` do Windows via ConPTY (cobre todas as formas de autenticação, `~/.ssh/config` e `known_hosts`). SSH.NET descartado.
- **MVP:** lista de hosts salvos (JSON local em `%LOCALAPPDATA%`), "Novo host" (apelido, host, porta, usuário, chave opcional), conectar abre o terminal próprio rodando `ssh`, desconectar. Fora do MVP: abas, SFTP, túneis, importar `~/.ssh/config`.
- **Renderização:** Win2D (`CanvasControl`), que usa Direct2D e DirectWrite por dentro.
- **Terminal no MVP:** só o mínimo (texto, cores ANSI, cursor, rolagem, redimensionamento). Tela alternativa (vim/tmux), mouse e seleção ficam para depois.
- **Sessões:** base preparada para várias sessões simultâneas (sem interface de abas no MVP). Hosts em JSON com campo `group` opcional.
- **Idiomas (14):** lista em `Models/LanguageOption.cs` (tag, nome na própria língua, bandeira em `Assets/Flags`): pt-BR (padrão), en-US, es-ES, fr-FR, de-DE, it-IT, ru-RU, zh-CN, ja-JP, ko-KR, ar-SA, hi-IN, tr-TR, nl-NL. Os 12 novos foram traduzidos por agente (máquina), sem revisão nativa. Árabe espelha a interface (`FlowDirection` no `MainWindow`); o terminal fica sempre da esquerda para a direita. Colar no terminal: Ctrl+V, Ctrl+Shift+V e botão direito (`PasteText.Clean` tira caracteres de controle).
- **Idioma (original):** pt-BR (padrão) e en-US, em `Strings/<idioma>/Resources.resw`. Sem texto fixo no código: tudo via `x:Uid` ou `Loc.Get`. Novo texto = adicionar a chave nos dois `.resw`. Troca de idioma em Configurações grava `AppSettings.Language` e vale após reiniciar (`PrimaryLanguageOverride` no construtor do `App`). Nomes de idioma ficam na própria língua, com bandeira em `Assets/Flags/*.svg`.
- **Dependências do MVP:** `CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Graphics.Win2D`.
- **Paleta:** fonte única em `Themes/Palette.xaml` (Dark e Light, tokens `*Color` e `*Brush`, ANSI 16 e cores do terminal), mesclada no `App.xaml`. Nunca usar cor fixa em tela ou no terminal: usar `{ThemeResource XxxBrush}` ou ler as chaves `*Color`. `AccentBrush` é para preenchimentos (botões); texto e indicadores sobre fundo escuro usam `AccentForegroundBrush`.
- **Cores (marca v2):** só azul-escuro `#1E3A8A` / azul-noite `#0F1F57` / azul do topo `#2747A6`, preto `#05070D` e branco. Claro: botões e destaque `#1E3A8A`. Escuro: fundo preto-azulado, botões `#2747A6`, texto/indicadores de destaque em `AccentForeground` (azul claro derivado). Sem outras cores de destaque; vermelho só para erro; cores ANSI do terminal são funcionais.
- **Regra de design (ícones):** ícones nunca têm fundo exclusivo (sem badge/quadrado atrás). Usam o fundo do componente onde estão (card, botão, linha, barra).
- **Terminal:** uma única superfície, sem campo de input separado: digita-se direto no prompt, com cursor piscando.
- **Visual:** base no estilo do **WinUI 3 Gallery** (Fluent). Não usar nenhum terminal como base visual. Temas **escuro e claro** (os dois).
- **Layout:** `NavigationView` com sidebar e tela inicial. Ao clicar em **Conectar**, a lista de hosts some, o terminal ocupa a área principal e a sidebar fica recolhida.
- **Fluxo de abertura:** splash, depois tela de boas-vindas explicando o app, depois um tour guiado usando um host de teste.
- **Roadmap (em decisão, não implementar ainda):** personalização e IA. Ver [ROADMAP.md](ROADMAP.md).

## Assets
- `Assets/`: conjunto MSIX (tiles, splash, Store, ícones em várias escalas).
- `brand/`: kit de marca (logo SVG, png, ico, animações Lottie, scripts geradores). `ApplicationIcon` aponta para `brand\ico\ClientSSH.ico`.
- Procedência da marca (conferida): o kit v2 é gerado por scripts próprios (`brand/_gerador`, símbolo desenhado em `mark.py`); não há mais nenhum arquivo do ícone de terceiros nem `brand-hover-pinch` no repo. O nome usa Inter (OFL) só em curvas, citado em `THIRD_PARTY_NOTICES.md`.

## Estrutura do código
Projeto único na raiz (sem pasta aninhada). Layout completo na tabela do [README.md](README.md#project-layout).
- `Models/`, `ViewModels/`, `Views/`, `Themes/Palette.xaml`, `Strings/<idioma>/Resources.resw`.
- `Services/` por módulo, com namespace igual à pasta: `Storage` (JSON local), `Shell` (navegação, diálogos, tema, bandeja, iniciar com o Windows), `Sessions` (`SessionManager`/`SessionTab`: abas e sessões simultâneas), `Updates`, `Localization` (`Loc`), `Demo` (host de demonstração do tour).
- `Terminal/`: motor do terminal sem tipos de UI: `ITerminalProcess` (`ConPtyProcess` para o ssh real, `SimulatedProcess` para o tour), `TerminalSession`, `VtScreen` (buffer em `VtScreen.cs`, parser de escapes em `VtScreen.Parser.cs`, tipos de célula em `Cell.cs`), `PseudoConsole` (P/Invoke em `PseudoConsole.Native.cs`), `SshCommand`. A página de sessões usa um `TabView` só como barra de abas e uma única `SessionView` abaixo (o `TerminalView` cria o canvas Win2D ao carregar e libera ao descarregar). `tests/ClientSSH.Tests` compila esses arquivos direto (`<Compile Include>` no `.csproj`): ao mover ou dividir um deles, atualizar o `.csproj` dos testes.
- `Views/TerminalView` em partes: `.xaml.cs` (ciclo de vida e tamanho), `.Drawing.cs` (Win2D), `.Input.cs` (teclado, colar, roda); auxiliares `TerminalPalette` (cores do `Palette.xaml`), `CursorBlink`, `TerminalKeys` (tecla -> sequência VT).
- `Services/Assistant`: `AssistantService` (validação, HTTP, erros), `AiRequests` (formato de cada provedor), `IAssistantService.cs` (contrato e resultados).
- **MVVM:** code-behind só com o que é da tela (desenho, ponteiro, teclado, foco); ações vão para comandos do ViewModel. ViewModels dependem de interfaces (`ISessionManager`, `IThemeService`, `ITrayService`, `IAiKeyStore`, `ISettingsStore`, ...); o DI no `App.xaml.cs` registra o tipo concreto e aponta a interface para a mesma instância. Interface nova fica no mesmo arquivo do serviço (exceto `ISessionManager.cs`).
- Arquivos grandes: dividir por responsabilidade em `partial` (`Classe.Parte.cs`) ou tipos próprios, com namespace igual à pasta.
- ViewModels usam `[ObservableProperty] public partial` (exige `LangVersion preview`, para ser compatível com WinRT/AOT).
- Textos de tela via `x:Uid` + `.resw` ou `Loc.Get`, nunca fixos. Toda chave nova vai nos dois idiomas.
- Documentação open source: `README.md`, `README.pt-BR.md`, `CONTRIBUTING.md`, `.editorconfig`.

## Estado
- Esqueleto pronto e compilando (`dotnet build -p:Platform=x64`): splash, boas-vindas (só na primeira abertura), lista de hosts com "Novo host", sidebar que recolhe ao conectar, página de terminal placeholder.
- **Rodar pelo Visual Studio (F5).** Para rodar fora do VS: `dotnet build -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:OutDir=<pasta>/`.
- Visual: tema próprio sobre WinUI 3 (fundo em gradiente da marca, cards arredondados, destaque Menta). Controles do WinUI copiam a cor de destaque para chaves próprias no load: ao estilizar um controle novo, sobrescrever as chaves dele em `Themes/Palette.xaml` (ex.: `AccentButtonBackground`).
- Tour guiado implementado (`TourViewModel` + `TeachingTip`): 4 passos (novo host, host demo, terminal simulado, sidebar). Só na primeira abertura. Host demo (`TourViewModel.DemoHost`) vive só em memória e o shell simulado (`SimulatedShell`) roda sem rede.
- Testado rodando (build sem pacote, capturas via PrintWindow/UIAutomation): splash, boas-vindas, tour, terminal demo, configurações, tema claro/escuro, inglês. Faltam: terminal real (Win2D + ConPTY), animação do splash, opção "Rever tour" nas configurações.
- Nenhum commit ainda.
- Dados persistentes: o manifesto desliga a virtualização de arquivos (`FileSystemWriteVirtualization=disabled` + capability `unvirtualizedResources`). Sem isso, o F5 do Visual Studio reinstala o pacote e apaga hosts/configurações guardados na pasta virtualizada do pacote.
- **Animações prontas:** `Views/LottieView` toca as animações Lottie da marca (`brand/anim`, copiadas como `Assets/Animations`, variante `.dark.json` no tema escuro; respeita "efeitos de animação" do Windows). Pacote `CommunityToolkit.WinUI.Lottie` 8.1.240821 (a 8.2+ exige .NET 9). Carregar por `StorageFile` + `SetSourceAsync` (o `CreateFromString` espera uma URI, não JSON). Usos: splash e boas-vindas (`logo-reveal`), estados vazios (`terminal-cursor`, `running-command`), conexão da sessão (`connecting`/`reconnecting` até a primeira saída, depois `connected`), verificação de atualização (`loading-ring`).
- **Regra de design (animações):** animar onde der: entrada de páginas e cartões (`EntranceThemeTransition` em `ChildrenTransitions`), linhas de lista (`ItemContainerTransitions`), navegação (`DrillIn` ao conectar, `Entrance` nas demais), splash com fade. Preferir transições nativas do WinUI a código.
- **Forma:** arredondamento moderado e consistente. Tokens em `Themes/Palette.xaml`: `ControlCornerRadius`=8 (controles), `OverlayCornerRadius`=12 (diálogos), `CardCornerRadius`=12 (cartões e superfícies). Nunca usar raio literal nem botão em pílula.
- **Componentes modernos:** preferir os mais novos do WinUI 3: `TitleBar`, `InfoBar`, `AnimatedIcon`, `TeachingTip`, `NavigationView`. A janela entra com fade + subida (`OpacityTransition`/`TranslationTransition` no `Root`).
- **Atualização automática (decisão do usuário):** além do botão, o app consulta o GitHub sozinho toda vez que abre (`AppSettings.AutoUpdateCheck`, ligado por padrão, com opção para desligar em Configurações); depois de um fim anormal (`RunMarker`: arquivo `running.marker` que sobra se o processo não saiu normal) consulta mesmo com a opção desligada. Se houver versão nova, mostra um `InfoBar` com "Atualizar agora"; só baixa e instala depois do clique. O download (só URLs `github.com/<repo>/releases/download/`) é conferido com `SHA256SUMS.txt` da release (`Sha256Sums`), e o instalador roda com `/SILENT`. O release precisa ter `ClientSSH-Setup-X.Y.Z.exe` e `SHA256SUMS.txt`.
- **Assistente de IA (implementado a pedido do usuário):** painel lateral na página de sessões (`AssistantViewModel`, `Services/Assistant`). Provedores: Claude, OpenAI, Gemini, Groq (OpenAI e Groq usam o mesmo formato). Chave do próprio usuário, criptografada com DPAPI (`AiKeyStore`, `ai-keys.json`); modelo editável (ids em `AiProviders.DefaultModel` envelhecem: o botão "Listar modelos disponíveis" consulta o provedor com a chave do usuário e `ModelList` filtra; um 404 sugere trocar de modelo). Envia só o prompt do usuário + o system prompt fixo (nunca terminal, hosts ou histórico). A resposta passa por `CommandText.Clean` (1ª linha, sem cercas nem caracteres de controle) e é digitada no prompt **sem Enter**. Só roda quando o usuário envia.
- **Janela:** `WindowStateService` abre centralizada na 1ª vez e grava posição, tamanho e maximizada em `AppSettings.Window` (px físicos, salva 800 ms após o último movimento e ao fechar; só restaura se a barra de título cair em algum monitor, senão centraliza).
- **Acesso remoto tipo AnyDesk (decisão do usuário: número fixo por PC, sem servidor nenhum, aviso mesmo minimizado, Full HD 60 fps):** `Services/Remote` + `RemoteViewModel` + `Views/RemotePage`. Sem biblioteca externa (a SIPSorcery foi descartada pela licença com restrição regional). **Número:** `RemoteIdentity` = chave ECDSA P-256 por instalação (`remote-identity.bin`, DPAPI), número de 9 dígitos = SHA-256 da chave pública mod 10^9 (sem HWID). **Achar o PC:** `DhtClient` (BEP 5, get_peers/announce_peer com implied_port, nó read-only) na DHT pública do BitTorrent; chave "host" = onde o PC anuncia o endereço (a cada 10 min), chave "call" = onde quem chama deixa o endereço (o PC vigia a cada 4 s e manda um "knock" para abrir o NAT). Na mesma rede, pergunta multicast 239.255.77.77:47801 (o roteador pode não ter hairpin). Mesmo socket UDP para DHT e sessão (`RemoteNode`), então o endereço anunciado é a porta certa. **Sessão:** `RemoteSession`: handshake em claro (chaves efêmeras P-256; o host assina com a chave do número e quem chama confere número + assinatura), ECDH + HKDF, AES-256-GCM com janela anti-replay (`RemoteChannel`), código de segurança de 6 dígitos. Escolha Controle/Só ver/Negar a cada pedido; nada é enviado antes. `AppSettings.RemoteListen` (padrão ligado) publica o IP junto do número; desligar para de publicar. Pedido com o app minimizado/na bandeja: `TrayService.DemandAttention` (restaura, fica por cima, pisca, notificação do Windows se der). **Vídeo:** `ScreenSource` = Desktop Duplication (`DesktopDuplication`, vtable COM crua; GDI como reserva e para telas maiores que Full HD) + cursor desenhado + BGRA→NV12 + `H264Encoder` (Media Foundation, software, CBR 8 Mbit/s, GOP 2 s, baixa latência); só manda quadro quando a tela ou o cursor mudam. Quem vê: `RemoteViewer` (thread própria, `H264Decoder`, NV12→BGRA) e Win2D na página. Perda de pacote: o viewer pede key frame (`PacketType.KeyRequest`) e descarta até chegar um. Medido aqui: captura 2 ms + conversão 1,5 ms + codificação 3 ms por quadro 1080p. Limites: CGNAT/NAT simétrico não conectam (sem relay), só monitor principal, sem som/área de transferência, protocolo sem revisão externa.
- **Atualizações:** botão manual em Configurações consulta `https://api.github.com/repos/<repo>/releases/latest`. Nunca roda sozinho, não envia dados do usuário e só abre links `https://github.com`. Exige uma Release publicada no GitHub com tag tipo `v1.2.0`.
- **Iniciar com o Windows / bandeja:** `StartupService` (MSIX: `StartupTask` do manifesto; sem pacote: chave Run do usuário com `--startup`; o estado vem do SO). `TrayService` (H.NotifyIcon.WinUI 2.3.0, a 2.4+ exige .NET 10) cria o ícone só quando "iniciar minimizado" ou "manter na bandeja ao fechar" estão ligados; o ícone usa `Assets/ClientSSH.ico` (um PNG derruba o app). Iniciar minimizado só vale quando o Windows abre o app.
- **Repositório de atualizações:** editável em Configurações (`AppSettings.UpdateRepo`, padrão `eupsa/client-ssh`), validado como `dono/nome` antes de entrar na URL. Projeto open source: forks podem apontar para o próprio repo.
- **Bandeiras:** agora são os SVG oficiais do flag-icons (MIT); licença em `THIRD_PARTY_NOTICES.md`.
- **Terminal real:** `Terminal/PseudoConsole.cs` (ConPTY via P/Invoke), `TerminalSession` (roda o comando e alimenta o `VtScreen`), `VtScreen` (parser VT/ANSI sem tipos de UI: cores 16/256/RGB, cursor, apagar, regiões de rolagem, tela alternativa, scrollback de 5000 linhas, respostas a `ESC[6n`) e `Views/TerminalView` (Win2D: desenho, teclado, roda do mouse, redimensionar). O comando é `ssh.exe -p <porta> -- <usuario>@<host>` (`SshCommand`); host e usuário são validados (sem '-' inicial, só caracteres seguros) antes de ir para a linha de comando. A página mata o ssh ao sair (`OnNavigatedFrom`). Testes do parser: `tests/ClientSSH.Tests` (`dotnet test`). Fora do escopo ainda: seleção, copiar/colar, mouse, Alt+tecla, caracteres de largura dupla.
- **Favoritos e histórico (SQLite):** `Database` (esquema com `PRAGMA user_version`), `HostStore`, `HistoryStore`, `LegacyHostsImport`. Favoritos vão ao topo da lista. O histórico é **opt-in** (`AppSettings.SaveHistory`, padrão desligado), guarda só host e data/hora (nunca comandos nem saída), tem limite de 1000 linhas e botão de apagar em Configurações.
- **Instalador:** só `Setup.exe` (Inno Setup, `installer/ClientSSH.iss` + `installer/build.ps1`), por usuário, sem MSIX/Store, sem assinatura. O assistente segue o idioma do Windows em 12 idiomas (en, pt-BR, es, fr, de, it, ru, ja, ko, ar, tr, nl, com os `.isl` que vêm no Inno Setup); chinês e hindi não têm tradução no Inno e caem em inglês. Publica build sem pacote (`WindowsPackageType=None`, self-contained, sem trim). O publish mexe no `obj/`: refazer o build empacotado depois. Release é publicada à mão no GitHub (sem minutos de Actions).
- **Docs open source:** README (en/pt-BR), CONTRIBUTING, CHANGELOG, SECURITY (relato privado pelo GitHub), CODE_OF_CONDUCT, templates em `.github/`.
