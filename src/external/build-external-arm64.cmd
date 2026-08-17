@echo off
call .\build-capstone-arm64.cmd || exit /b 1
call .\build-graphviz-arm64.cmd || exit /b 1
call .\build-tree-sitter-arm64.cmd || exit /b 1