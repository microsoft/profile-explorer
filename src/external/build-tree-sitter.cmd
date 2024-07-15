cd tree-sitter
rmdir /s /q build
rmdir /s /q out
mkdir build
nmake
copy out\*.dll build\*
cd ..
