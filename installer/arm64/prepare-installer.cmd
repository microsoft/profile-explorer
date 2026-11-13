set %_VERSION="1.4.1"

iscc.exe installer.iss /DAPP_VERSION=%_VERSION% /O%cd%
