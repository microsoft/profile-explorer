set %_VERSION="1.2.0"

iscc.exe installer.iss /DAPP_VERSION=%_VERSION% /O%cd%
