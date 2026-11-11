# Instruções para agentes de código

Projeto: **MiniPrint Desktop** (WPF, .NET 8, impressoras térmicas BLE "CAT Printer").

## Regra de entrega: sempre gerar o instalador

**Toda alteração de código só está concluída quando os artefatos de distribuição
forem regerados.** Depois de qualquer correção, ajuste ou nova funcionalidade:

1. Compile: `dotnet build MiniPrint.sln -c Release`
2. Rode os testes: `dotnet test tests\MiniPrint.Tests\MiniPrint.Tests.csproj`
3. **Gere o instalador e o portátil:**

   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File scripts\build-release.ps1
   ```

   O script faz tudo: build Release, testes, publish self-contained (arquivo
   único), autoverificação da interface (`--selftest`), ZIP portátil e o
   instalador Inno Setup.

4. Entregue os artefatos de `dist\`:
   - `dist\MiniPrint-Setup-<versão>.exe`
   - `dist\MiniPrintDesktop-<versão>-portable.zip`

Não anuncie a tarefa como concluída enquanto o instalador em `dist\` não
corresponder ao código alterado (confira a data/hora dos arquivos).

### Versão

A versão vem de `Directory.Build.props` (`<Version>`). Ao publicar uma correção
que o usuário vai instalar por cima da anterior, **suba a versão** (ex.: 1.0.0 →
1.0.1) antes de rodar o `build-release.ps1`; assim o instalador tem nome próprio
e o Windows trata como atualização, não como reinstalação da mesma versão.

## Ambiente

- O SDK do .NET **não está no `PATH`** nesta máquina. Use
  `%LOCALAPPDATA%\dotnet-sdk\dotnet.exe` ou deixe o `build-release.ps1`
  resolvê-lo (ele já procura nesse diretório).
- `EnableWindowsTargeting` já está ligado; WPF, BLE (Windows SDK) e testes
  rodam em `net8.0-windows10.0.19041.0`.
- Dados do usuário ficam em `%LOCALAPPDATA%\MiniPrintDesktop`.
- Ferramenta de diagnóstico: `src\MiniPrint.Diagnostics` (comandos `scan`,
  `status`, `print`, ... — ver `docs/DIAGNOSTICO.md`).

## Estilo

- Código e comentários em português (pt-BR), como o restante do projeto.
- ViewModels em `src\MiniPrint.Desktop\ViewModels`, telas em `Views`.
- Regra de ouro da UI: **não misture estados diferentes na mesma propriedade**
  (ex.: resultado de busca BLE e estado da conexão já causaram bug — use
  propriedades separadas).

## Testes

- `tests\MiniPrint.Tests` (xUnit). Rode tudo antes de gerar a release.
- Testes que dependem de hardware real devem ficar fora da suíte padrão; use o
  modo simulado (`UseSimulator`) ou transporte falso.
