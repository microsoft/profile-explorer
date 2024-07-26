set _EXTERNALS_PATH="%~dp0\.."
set _OUT_PATH="%~dp0\out_arm64"
set _RESULT_PATH="%~dp0\weights_arm64"

rd %_RESULT_PATH% /s /q
mkdir %_RESULT_PATH%

call copy-graphviz-arm64.cmd
cd %_OUT_PATH%

for /r %%f in (..\training\*) do (
	dot.exe -Tplain %%f > null
)

for /r %%f in (*.pgd) do (
	pgomgr /merge %%f
	copy %%f %_RESULT_PATH%
)

cd ..