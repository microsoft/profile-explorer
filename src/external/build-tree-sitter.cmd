cd tree-sitter
cd csharp-tree-sitter
git submodule update --init --recursive
cd ..

rmdir /s /q build
rmdir /s /q out
mkdir build
nmake
copy out\*.dll build\*
cd ..
