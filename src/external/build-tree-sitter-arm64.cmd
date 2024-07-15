cd tree-sitter
rmdir /s /q build
rmdir /s /q out
mkdir build_arm64
nmake
copy out\*.dll build_arm64\*
cd ..
