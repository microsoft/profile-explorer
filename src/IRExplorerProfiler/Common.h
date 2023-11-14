#pragma once

#include <stdint.h>
#include <unknwn.h>

#ifdef _WINDOWS
#include <atlbase.h>
#else
typedef int32_t HRESULT;
struct CAtlException {
	HRESULT HResult;
	CAtlException(HRESULT hr) : HResult(hr) {}
};
#define AtlThrow(hr) throw CAtlException(hr)
#if defined(_DEBUG) && !defined(ATLASSERT)
#define ATLASSERT(expr) _ASSERTE(expr)
#endif // ATLASSERT

#include <atl.h>
#endif

#define HR(x) { auto _hr = (x); if(FAILED(_hr)) return _hr; }
