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
- Paleta de comandos (**Ctrl+Shift+P**, ou o botão de busca na barra de título): digite para achar qualquer ação, como abrir um host salvo, fechar ou restaurar uma aba, trocar o tema, ir para uma página ou verificar atualizações
- Abas: várias sessões simultâneas, que continuam ativas enquanto você navega por hosts e configurações
- Configurações: idioma, tema, tamanho da fonte do terminal, iniciar com o Windows, iniciar minimizado, manter na bandeja ao fechar
- Verificação de atualizações nas Releases do GitHub de um repositório que você pode trocar, com atualização em um clique (o download é conferido com o `SHA256SUMS.txt`)

O [ROADMAP.md](ROADMAP.md) lista o que está planejado (integração visual com o Windows, entre outros).

## Privacidade

O ClientSSH não faz conexões por conta própria, com quatro exceções: as sessões SSH que você inicia, a verificação de atualizações, o assistente de IA (só quando você envia um pedido) e o acesso remoto (enquanto **Aceitar pedidos de acesso** está ligado, o padrão, o app mantém este PC encontrável pelo número na DHT pública do BitTorrent; veja abaixo). A verificação roda quando você clica em **Verificar atualizações** e toda vez que o app abre (dá para desligar a automática em Configurações). Ela consulta a API do GitHub pela última versão do repositório definido em Configurações (padrão `eupsa/client-ssh`) e não envia mais nada. Se houver versão nova, o app só mostra um aviso; ele baixa e instala depois que você clica em **Atualizar agora**.

Os dados ficam em `%LOCALAPPDATA%\ClientSSH` (`clientssh.db` com hosts, favoritos e histórico, e `settings.json`). Defina a variável de ambiente `CLIENTSSH_DATA_DIR` para usar outra pasta.

## Instalar

Baixe o `ClientSSH-Setup-<versão>.exe` na página de [Releases](https://github.com/eupsa/client-ssh/releases) e execute. O instalador fala o idioma do seu Windows quando é um de 12 (inglês nos demais). Ele instala só para o seu usuário (sem direitos de administrador) e não apaga seus dados ao desinstalar.

O instalador não é assinado digitalmente, então o Windows SmartScreen pode avisar de editor desconhecido: escolha **Mais informações** e **Executar assim mesmo**. Você pode gerar o instalador você mesmo, veja [Build the installer](CONTRIBUTING.md#build-the-installer).

## Acesso remoto (tipo AnyDesk, sem servidor)

Veja e controle outro PC com Windows, ou deixe alguém ajudar você neste, **direto entre os dois PCs**: sem servidor do ClientSSH, sem conta, sem relay.

1. Cada instalação tem um **número do PC** fixo (9 dígitos, como `847 291 503`), em **Remoto** > **Este PC**. Ele vem de uma chave criada uma vez neste PC (guardada criptografada com o DPAPI do Windows), não de dados do hardware, e continua o mesmo nas atualizações.
2. Seu amigo passa o número dele. Digite em **Acessar outro PC** e clique em **Conectar**.
3. No PC dele aparece um pedido, mesmo com o ClientSSH minimizado ou na bandeja: a janela vem para a frente, fica por cima das outras e o botão na barra de tarefas pisca. Ele escolhe **Permitir controle**, **Só visualizar** ou **Negar**. As duas telas mostram o mesmo **código de segurança** de seis dígitos: leiam em voz alta, e se for diferente, neguem.
4. Enquanto uma tela está compartilhada, um aviso **Parar de compartilhar** fica visível em todas as páginas do PC compartilhado.

A imagem é H.264 em até 1920 x 1080 e 60 quadros por segundo, codificada e decodificada pelos codecs que já vêm no Windows (Media Foundation) e capturada com Desktop Duplication; só as mudanças são enviadas, então uma tela parada quase não gasta nada. Uma tela movimentada usa uns 3 a 8 Mbit/s.

Como um PC é achado sem servidor: enquanto **Aceitar pedidos de acesso** está ligado (o padrão), o ClientSSH guarda o endereço público deste PC, numa chave derivada do número, na DHT pública do BitTorrent, a rede de milhões de PCs que os clientes de BitTorrent usam para se achar, sem dono e sem servidor central. Quem chama procura o número lá, deixa o próprio endereço numa segunda chave que o PC chamado vigia, e os dois mandam pacotes um para o outro ao mesmo tempo para abrir caminho pelos roteadores (UDP hole punching). Na mesma rede, os dois PCs também se acham por uma pergunta multicast local. Só o PC dono do número consegue responder: ele assina o handshake com a chave por trás do número. Daí em diante é ponta a ponta: chaves descartáveis por sessão (ECDH P-256 + HKDF-SHA256), cada pacote cifrado e autenticado (AES-256-GCM).

Limites, com honestidade:
- **Sem nenhum servidor não dá para funcionar em toda rede.** Se um dos lados usa CGNAT ou NAT simétrico (comum em dados móveis e em alguns provedores), o caminho direto pode nunca abrir; o AnyDesk resolve isso com servidores de relay, que o ClientSSH não tem. O app avisa quando isso acontece. Tente outra rede ou o ponto de acesso do celular.
- **Seu IP público fica publicado junto com o seu número** na DHT enquanto os pedidos estão ligados, então quem souber (ou chutar) o seu número descobre o seu IP. Mesmo assim, ninguém vê nada sem o seu OK. Desligue **Aceitar pedidos de acesso** para parar de publicar.
- Os pedidos só chegam com o ClientSSH rodando (aberto ou na bandeja). O Windows pode perguntar uma vez se libera o ClientSSH na rede.
- Só o monitor principal, sem som, sem área de transferência nem transferência de arquivos por enquanto. O teclado vai como códigos de tecla, então layouts diferentes nos dois PCs podem digitar caracteres diferentes. O Windows não permite entrada em janelas de administrador nem na área de trabalho segura (avisos do UAC).
- O protocolo é novo e não teve revisão de segurança externa. Revisões são bem-vindas, veja o [SECURITY.md](SECURITY.md).

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
