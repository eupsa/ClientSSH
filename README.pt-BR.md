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
- Verificação manual de atualizações nas Releases do GitHub de um repositório que você pode trocar

O [ROADMAP.md](ROADMAP.md) lista o que está planejado (integração visual com o Windows, entre outros).

## Privacidade

O ClientSSH não faz conexões por conta própria. As únicas requisições são as sessões SSH que você inicia e a verificação de atualizações que você aciona em **Verificar atualizações**, que consulta a API do GitHub pela última versão do repositório definido em Configurações (padrão `eupsa/client-ssh`) e não envia mais nada.

Os dados ficam em `%LOCALAPPDATA%\ClientSSH` (`clientssh.db` com hosts, favoritos e histórico, e `settings.json`). Defina a variável de ambiente `CLIENTSSH_DATA_DIR` para usar outra pasta.

## Instalar

Baixe o `ClientSSH-Setup-<versão>.exe` na página de [Releases](https://github.com/eupsa/client-ssh/releases) e execute. Ele instala só para o seu usuário (sem direitos de administrador) e não apaga seus dados ao desinstalar.

O instalador não é assinado digitalmente, então o Windows SmartScreen pode avisar de editor desconhecido: escolha **Mais informações** e **Executar assim mesmo**. Você pode gerar o instalador você mesmo, veja [Build the installer](CONTRIBUTING.md#build-the-installer).

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
