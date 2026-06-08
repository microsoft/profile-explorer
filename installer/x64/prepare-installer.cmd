set %_VERSION="1.3.0"

iscc.exe installer.iss /DAPP_VERSION=%_VERSION% /O%cd%
