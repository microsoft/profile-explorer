########
#
#	Tree-Sitter Base and Common Language Parsers
#

CFLAGS=/nologo /FC /O2 /GL /Ob3 /Zc:inline /Gm- /Oi /Z7 /Gy /diagnostics:column /Icsharp-tree-sitter/tree-sitter/tree-sitter/lib/include
LFLAGS=/def:csharp-tree-sitter/tree-sitter/$(@B).def /incremental:no /debug  /OPT:REF /OPT:ICF /LTCG
LFLAGS2=/def:$(@B).def /incremental:no /debug  /OPT:REF /OPT:ICF /LTCG

BIN=out

DLLS=\
	$(BIN)/tree-sitter.dll \
	$(BIN)/tree-sitter-cpp.dll \
	$(BIN)/tree-sitter-c-sharp.dll \
	$(BIN)/tree-sitter-rust.dll \
	
all: dirs $(DLLS)

dirs:
	@if not exist $(BIN)\nul mkdir $(BIN)

########
#
# 	Tree-Sitter Base Library
#
$(BIN)/tree-sitter.obj: \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/alloc.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/get_changed_ranges.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/language.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/lexer.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/lib.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/node.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/parser.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/query.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/stack.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/subtree.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/tree.c \
		csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/tree_cursor.c
	cl $(CFLAGS) /Fo:$@ \
		/Icsharp-tree-sitter/tree-sitter/tree-sitter/lib/src /Icsharp-tree-sitter/tree-sitter/tree-sitter/lib/src/unicode \
		/c csharp-tree-sitter/tree-sitter/tree-sitter/lib/src/lib.c

$(BIN)/tree-sitter.dll: $(BIN)/tree-sitter.obj
	cl /LD $(CFLAGS) /Fe:$@ $** /link $(LFLAGS)


########
#
#   C++
#
$(BIN)/tree-sitter-cpp-parser.obj: csharp-tree-sitter/tree-sitter/tree-sitter-cpp/src/parser.c
	cl $(CFLAGS) /Fo:$@ /Icsharp-tree-sitter/tree-sitter/tree-sitter-cpp/src/include /c $**

$(BIN)/tree-sitter-cpp-scanner.obj: csharp-tree-sitter/tree-sitter/tree-sitter-cpp/src/scanner.c
	cl $(CFLAGS) /Fo:$@ /Icsharp-tree-sitter/tree-sitter/tree-sitter-cpp/src/include /c $**

$(BIN)/tree-sitter-cpp.dll: $(BIN)/tree-sitter-cpp-parser.obj $(BIN)/tree-sitter-cpp-scanner.obj
	cl /LD $(CFLAGS) /Fe:$@ $** /link $(LFLAGS)

########
#
#   C#
#
$(BIN)/tree-sitter-c-sharp-parser.obj: tree-sitter-c-sharp/src/parser.c
	cl $(CFLAGS) /Fo:$@ /Itree-sitter-c-sharp/src/include /c $**

$(BIN)/tree-sitter-c-sharp-scanner.obj: tree-sitter-c-sharp/src/scanner.c
	cl $(CFLAGS) /Fo:$@ /Itree-sitter-c-sharp/src/include /c $**

$(BIN)/tree-sitter-c-sharp.dll: $(BIN)/tree-sitter-c-sharp-parser.obj $(BIN)/tree-sitter-c-sharp-scanner.obj
	cl /LD $(CFLAGS) /Fe:$@ $** /link $(LFLAGS2)

########
#
#   Rust
#
$(BIN)/tree-sitter-rust-parser.obj: tree-sitter-rust/src/parser.c
	cl $(CFLAGS) /Fo:$@ /Itree-sitter-rust/src/include /c $**

$(BIN)/tree-sitter-rust-scanner.obj: tree-sitter-rust/src/scanner.c
	cl $(CFLAGS) /Fo:$@ /Itree-sitter-rust/src/include /c $**

$(BIN)/tree-sitter-rust.dll: $(BIN)/tree-sitter-rust-parser.obj $(BIN)/tree-sitter-rust-scanner.obj
	cl /LD $(CFLAGS) /Fe:$@ $** /link $(LFLAGS2)

########
#
#	Clean
#
clean:
	-del *.obj $(BIN)\*.obj 2>nul
	-del $(BIN)\tree-sitter*.dll $(BIN)\tree-sitter*.exp $(BIN)\tree-sitter*.lib $(BIN)\tree-sitter*.pdb 2>nul
	-del tree-sitter*.dll tree-sitter*.exp tree-sitter*.lib tree-sitter*.pdb 2>nul
	-del $(BIN)\xred.* $(BIN)\test.exe $(BIN)\test.exp $(BIN)\test.pdb $(BIN)\test.lib 2>nul
	-del *~ 2>nul
	@-echo.
	@-echo.