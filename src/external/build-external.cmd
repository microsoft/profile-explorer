@echo off
call .\build-capstone.cmd || exit /b 1
call .\build-graphviz.cmd || exit /b 1
call .\build-tree-sitter.cmd || exit /b 1