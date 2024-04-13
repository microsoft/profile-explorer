#include <mutex>
#include "Common.h"
#include "CoreProfilerFactory.h"
#include "CoreProfiler.h"

// COM ID required when loading the profiler DLL in the .NET runtime.
class __declspec(uuid("805A308B-061C-47F3-9B30-F785C3186E81")) CoreProfiler;

extern "C" BOOL __stdcall DllMain(HINSTANCE hInstDll, DWORD reason, PVOID) {
	switch (reason) {
	case DLL_PROCESS_ATTACH:
		Log(L"IRX: Profiler connected\n");
		break;

	case DLL_PROCESS_DETACH:
		break;
	}
	return TRUE;
}

extern "C" HRESULT __stdcall DllGetClassObject(REFCLSID rclsid, REFIID riid, void** ppv) {
	if (rclsid == __uuidof(CoreProfiler)) {
		static CoreProfilerFactory factory;
		return factory.QueryInterface(riid, ppv);
	}
	return CLASS_E_CLASSNOTAVAILABLE;
}
