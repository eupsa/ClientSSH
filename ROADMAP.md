# Roadmap

Itens abaixo ainda estão **em decisão**. Nada aqui está fechado nem implementado.

## Personalização (em decisão)
- Cliente SSH bem personalizável: temas, fontes, cores e atalhos.
- Configuração local em arquivo JSON, sem sincronização externa.

## IA para gerar comandos (em decisão)
- Opcional e opt-in. O usuário cola a própria API key e a IA gera o comando/código para executar no terminal.
- Ideias a validar:
  - Chave guardada no Windows (Credential Locker/DPAPI), nunca em texto puro nem no repo.
  - Independente de provedor, com chamada HTTPS direta do PC ao provedor escolhido. Seria a única comunicação externa do app, só se o usuário ativar.
  - O comando gerado é mostrado para revisão e nunca é executado automaticamente.
  - Enviar à IA só o pedido do usuário, sem saída do terminal, hosts, usuários nem segredos, a menos que o usuário inclua de propósito.

## Diferenciais do terminal (em decisão)
- **Assistente de comandos com IA:** o usuário descreve o que quer em linguagem natural (ex.: "encontre arquivos maiores que 500 MB") e o app sugere o comando correspondente (ex.: `find / -type f -size +500M`). Segue as regras da seção de IA acima: o usuário revisa e confirma, nada roda sozinho.
- **Workspaces de desenvolvimento:** agrupar hosts (ex.: "Produção", "Staging", "Cliente X") e abrir o grupo de uma vez. A definir: o que um workspace guarda além da lista de hosts (comandos iniciais, layout de abas, tema).

## Novos itens (em decisão)
- **Visual moderno integrado ao Windows:** Mica/Mica Alt no fundo, `TitleBar` nativo (já em uso), opção de seguir a cor de destaque do Windows (hoje a cor é fixa: Menta), jump list e notificações do sistema. Conflito a decidir: seguir o Windows x manter a cor fixa pedida antes.
- **Histórico e favoritos com SQLite:** FEITO (`clientssh.db`, histórico opt-in, migração do `hosts.json`). Ideia original: trocar `hosts.json` por um banco local (`Microsoft.Data.Sqlite`), com favoritos, grupos e histórico de conexões. Regras de privacidade: tudo local, histórico **opt-in** (hoje a regra é não gravar histórico por padrão), guardar só host e data/hora (nunca comandos nem saída), botão para apagar tudo, migração automática do `hosts.json`.
- **Abas e sessões simultâneas:** feito (`TabView` + `SessionManager`). Falta: avisar ao fechar o app com sessões abertas, reordenar abas (dividir painéis: feito).
- **Ordem sugerida:** 1) terminal real (ConPTY + Win2D), 2) abas e sessões, 3) SQLite com favoritos e histórico, 4) ajustes de integração visual com o Windows.

## Depois do MVP
- Abas, SFTP, túneis.
- Importar hosts do `~/.ssh/config` do Windows.
