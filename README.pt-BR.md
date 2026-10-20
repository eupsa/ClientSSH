# ClientSSH

Um cliente SSH simples e rápido para Windows. Código aberto, feito com WinUI 3.

[Read in English](README.md)

## Por quê

- **Seus dados ficam no seu PC.** Hosts e configurações são salvos localmente. Sem conta, sem nuvem e sem telemetria.
- **Conexão fácil.** Escolha um host e conecte. O login é feito pelo cliente OpenSSH do Windows: senha, chave privada, agente SSH, chave de segurança e o seu `known_hosts` funcionam como de costume.
- **Visual moderno no Windows.** WinUI 3, controles Fluent, temas claro e escuro, em português (Brasil) e inglês.

## Recursos

- Lista de hosts com favoritos e histórico de conexões opcional e local (desligado por padrão)
- Tour guiado na primeira abertura (o tour usa um host simulado, sem rede)
- Terminal real: `ssh.exe` num pseudo-console do Windows (ConPTY), desenhado com Win2D
- Abas: várias sessões simultâneas, que continuam ativas enquanto você navega por hosts e configurações
- Configurações: idioma, tema, tamanho da fonte do terminal, iniciar com o Windows, iniciar minimizado, manter na bandeja ao fechar
- Verificação de atualizações nas Releases do GitHub de um repositório que você pode trocar, com atualização em um clique (o download é conferido com o `SHA256SUMS.txt`)

O [ROADMAP.md](ROADMAP.md) lista o que está planejado (integração visual com o Windows, entre outros).

## Privacidade

O ClientSSH não faz conexões por conta própria, com quatro exceções: as sessões SSH que você inicia, a verificação de atualizações, o assistente de IA (só quando você envia um pedido) e o acesso remoto (só quando você inicia). A verificação roda quando você clica em **Verificar atualizações** e toda vez que o app abre (dá para desligar a automática em Configurações). Ela consulta a API do GitHub pela última versão do repositório definido em Configurações (padrão `eupsa/client-ssh`) e não envia mais nada. Se houver versão nova, o app só mostra um aviso; ele baixa e instala depois que você clica em **Atualizar agora**.

Os dados ficam em `%LOCALAPPDATA%\ClientSSH` (`clientssh.db` com hosts, favoritos e histórico, e `settings.json`). Defina a variável de ambiente `CLIENTSSH_DATA_DIR` para usar outra pasta.

## Instalar

Baixe o `ClientSSH-Setup-<versão>.exe` na página de [Releases](https://github.com/eupsa/client-ssh/releases) e execute. O instalador fala o idioma do seu Windows quando é um de 12 (inglês nos demais). Ele instala só para o seu usuário (sem direitos de administrador) e não apaga seus dados ao desinstalar.

O instalador não é assinado digitalmente, então o Windows SmartScreen pode avisar de editor desconhecido: escolha **Mais informações** e **Executar assim mesmo**. Você pode gerar o instalador você mesmo, veja [Build the installer](CONTRIBUTING.md#build-the-installer).

## Acesso remoto (PC para PC, sem servidor)

Opcional. Compartilhe a tela deste PC, ou veja e controle outro PC, **direto entre os dois**: sem servidor do ClientSSH, sem conta, sem relay.

1. No PC que será compartilhado, abra **Remoto** > **Criar um código** e envie o código para a outra pessoa (qualquer aplicativo de conversa).
2. No outro PC, **Remoto** > cole em **Conectar a um PC**. Aparece um código de resposta: envie de volta.
3. Cole o código de resposta no primeiro PC. A pessoa desse PC vê um **código de segurança** e escolhe **Permitir controle**, **Só visualizar** ou **Negar**. Leiam os seis dígitos em voz alta um para o outro (ligação ou áudio): se forem diferentes, há alguém no meio, escolha Negar.

Como funciona: os dois PCs perguntam a um servidor STUN público (o do Google e o da Cloudflare, por padrão; editável como `RemoteStunServers` no `settings.json`) qual é o seu endereço público e depois mandam pacotes um para o outro ao mesmo tempo, para abrir caminho pelos roteadores (UDP hole punching). O STUN só vê o endereço e a porta públicos, nunca o conteúdo. Daí em diante é ponta a ponta: um par de chaves descartável por sessão (ECDH P-256), chaves derivadas dos dois códigos (HKDF-SHA256), cada pacote cifrado e autenticado (AES-256-GCM).

Limites, com honestidade:
- **Sem nenhum servidor não dá para funcionar em toda rede.** Se uma das redes usa CGNAT ou NAT simétrico (comum em dados móveis e em alguns provedores), o caminho direto pode nunca abrir. O ClientSSH avisa quando detecta isso. Tente outra rede, uma VPN ou o ponto de acesso do celular.
- Só IPv4, só o monitor principal, quadros JPEG (cerca de 8 por segundo), sem som, sem área de transferência nem transferência de arquivos por enquanto.
- O teclado é enviado como códigos de tecla, então layouts diferentes nos dois PCs podem digitar caracteres diferentes. O Windows não permite injetar entrada em janelas de administrador nem na área de trabalho segura.
- O protocolo é novo e não teve revisão de segurança externa. Revisões são bem-vindas, veja o [SECURITY.md](SECURITY.md).
- Enquanto sua tela está compartilhada, um aviso com **Parar de compartilhar** fica visível em todas as páginas.

## Janela

A janela abre centralizada na primeira vez e depois lembra posição, tamanho e se estava maximizada.

## Assistente de IA

Opcional. Abra o painel na barra de abas, escolha Claude, ChatGPT, Gemini ou Groq, cole a sua chave de API e descreva o que quer. O comando devolvido é digitado no prompt do terminal **sem Enter**, para você ler e decidir. Só o seu texto e uma instrução fixa vão para o provedor escolhido: nunca a saída do terminal, os hosts ou o histórico. A chave fica criptografada com o DPAPI do Windows para a sua conta e só sai do PC na requisição ao provedor.

## Requisitos

- Windows 10 1809 ou superior (Windows 11 recomendado)
- Cliente OpenSSH (já vem no Windows 10/11)
- Para compilar: .NET 8 SDK e Visual Studio 2022 ou superior com a carga de trabalho **Desenvolvimento de aplicativos Windows**

## Compilar e executar

Abra `ClientSSH.slnx` no Visual Studio e pressione F5, ou pelo terminal:

```bash
dotnet build ClientSSH.csproj -p:Platform=x64
```

O app empacotado precisa ser iniciado pelo Visual Studio (exige o pacote MSIX). Para uma execução rápida sem pacote:

```bash
dotnet build ClientSSH.csproj -p:Platform=x64 -p:WindowsPackageType=None -p:WindowsAppSDKSelfContained=true -p:OutDir=out/
```

Testes:

```bash
dotnet test tests/ClientSSH.Tests
```

## Estrutura

Veja a tabela em [README.md](README.md#project-layout).

## Contribuindo

Leia o [CONTRIBUTING.md](CONTRIBUTING.md). Issues e pull requests são bem-vindos.

## Licença

[MIT](LICENSE). Avisos de terceiros em [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
