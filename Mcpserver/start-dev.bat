@echo off
setlocal enabledelayedexpansion

REM ====== CONFIG ======
set SERVICE_NAME=RabbitMQ
set RABBIT_PORT=5672
set MGMT_PORT=15672

REM Se quiser rodar um csproj específico, preencha:
REM set PROJECT_PATH=src\Mcpserver\Mcpserver.csproj
set PROJECT_PATH=

echo.
echo ==========================================
echo   Dev start: RabbitMQ + MCP Server
echo ==========================================
echo.

REM ====== 1) Start RabbitMQ service (requires Admin) ======
echo [1/4] Iniciando servico do RabbitMQ...
net start %SERVICE_NAME% >nul 2>&1

if %errorlevel% neq 0 (
  echo [WARN] Nao consegui iniciar o servico "%SERVICE_NAME%".
  echo        - Rode este .bat como Administrador.
  echo        - Ou verifique se o RabbitMQ esta instalado como servico.
) else (
  echo [OK] Servico "%SERVICE_NAME%" iniciado (ou ja estava rodando).
)

REM ====== 2) Wait for port 5672 ======
echo.
echo [2/4] Aguardando RabbitMQ abrir a porta %RABBIT_PORT%...
set /a tries=0

:wait_rabbit
set /a tries+=1
powershell -NoProfile -Command ^
  "try { $c = Test-NetConnection -ComputerName 127.0.0.1 -Port %RABBIT_PORT%; if($c.TcpTestSucceeded){ exit 0 } else { exit 1 } } catch { exit 1 }" >nul 2>&1

if %errorlevel%==0 (
  echo [OK] RabbitMQ pronto na porta %RABBIT_PORT%.
) else (
  if !tries! GEQ 30 (
    echo [ERRO] RabbitMQ nao abriu a porta %RABBIT_PORT% apos !tries! tentativas.
    echo       Verifique instalacao do RabbitMQ/Erlang e tente novamente.
    goto run_app_anyway
  )
  timeout /t 1 /nobreak >nul
  goto wait_rabbit
)

REM ====== 3) (Optional) Enable management plugin if available in PATH ======
echo.
echo [3/4] Verificando painel (management)...
powershell -NoProfile -Command ^
  "try { $c = Test-NetConnection -ComputerName 127.0.0.1 -Port %MGMT_PORT%; if($c.TcpTestSucceeded){ exit 0 } else { exit 1 } } catch { exit 1 }" >nul 2>&1

if %errorlevel%==0 (
  echo [OK] Painel ja esta ativo em http://localhost:%MGMT_PORT% (guest/guest)
) else (
  echo [INFO] Painel nao esta ativo na porta %MGMT_PORT%.
  echo        Se quiser habilitar: rabbitmq-plugins enable rabbitmq_management
)

:run_app_anyway
echo.
echo [4/4] Iniciando seu MCP Server...
echo.

if "%PROJECT_PATH%"=="" (
  dotnet run
) else (
  dotnet run --project "%PROJECT_PATH%"
)

endlocal
