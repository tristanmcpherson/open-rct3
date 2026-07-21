#include "BridgeProtocol.h"

#include <windows.h>
#include <sddl.h>
#include <d3d9.h>
#include <wincodec.h>

#include <algorithm>
#include <array>
#include <atomic>
#include <cstdint>
#include <cstring>
#include <mutex>
#include <sstream>
#include <string>
#include <vector>

namespace {

using Direct3DCreate9Fn = IDirect3D9* (WINAPI*)(UINT);
using CreateDeviceFn = HRESULT (STDMETHODCALLTYPE*)(IDirect3D9*, UINT, D3DDEVTYPE, HWND, DWORD,
  D3DPRESENT_PARAMETERS*, IDirect3DDevice9**);
using PresentFn = HRESULT (STDMETHODCALLTYPE*)(IDirect3DDevice9*, const RECT*, const RECT*, HWND,
  const RGNDATA*);
using SetTransformFn = HRESULT (STDMETHODCALLTYPE*)(IDirect3DDevice9*, D3DTRANSFORMSTATETYPE,
  const D3DMATRIX*);
using SetVertexShaderConstantFFn = HRESULT (STDMETHODCALLTYPE*)(IDirect3DDevice9*, UINT,
  const float*, UINT);

Direct3DCreate9Fn originalDirect3DCreate9 = nullptr;
CreateDeviceFn originalCreateDevice = nullptr;
PresentFn originalPresent = nullptr;
SetTransformFn originalSetTransform = nullptr;
SetVertexShaderConstantFFn originalSetVertexShaderConstantF = nullptr;
std::atomic<bool> captureRequested{false};
HANDLE captureComplete = nullptr;
std::mutex stateMutex;
std::vector<uint8_t> capturedPng;
std::string captureError;
D3DMATRIX viewMatrix{};
D3DMATRIX projectionMatrix{};
bool hasView = false;
bool hasProjection = false;
bool hasVertexShaderConstants = false;
UINT lastVertexShaderStartRegister = 0;
UINT lastVertexShaderVectorCount = 0;
std::array<float, 64> lastVertexShaderConstants{};
std::atomic<bool> hasDevice{false};
std::atomic<HWND> deviceWindow{nullptr};

bool PatchPointer(void** target, void* replacement, void** original) {
  DWORD oldProtection = 0;
  if (!VirtualProtect(target, sizeof(void*), PAGE_EXECUTE_READWRITE, &oldProtection)) return false;
  const auto current = InterlockedExchangePointer(target, replacement);
  DWORD ignored = 0;
  VirtualProtect(target, sizeof(void*), oldProtection, &ignored);
  FlushInstructionCache(GetCurrentProcess(), target, sizeof(void*));
  if (original && current != replacement) *original = current;
  return true;
}

std::string Base64(const std::vector<uint8_t>& bytes) {
  static constexpr char alphabet[] =
    "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
  std::string output;
  output.reserve((bytes.size() + 2) / 3 * 4);
  for (size_t offset = 0; offset < bytes.size(); offset += 3) {
    const auto remaining = bytes.size() - offset;
    const auto value = static_cast<uint32_t>(bytes[offset]) << 16 |
      (remaining > 1 ? static_cast<uint32_t>(bytes[offset + 1]) << 8 : 0) |
      (remaining > 2 ? bytes[offset + 2] : 0);
    output.push_back(alphabet[(value >> 18) & 63]);
    output.push_back(alphabet[(value >> 12) & 63]);
    output.push_back(remaining > 1 ? alphabet[(value >> 6) & 63] : '=');
    output.push_back(remaining > 2 ? alphabet[value & 63] : '=');
  }
  return output;
}

std::vector<uint8_t> EncodePng(IDirect3DDevice9* device) {
  IDirect3DSurface9* backbuffer = nullptr;
  if (FAILED(device->GetBackBuffer(0, 0, D3DBACKBUFFER_TYPE_MONO, &backbuffer)))
    throw std::runtime_error("GetBackBuffer failed");
  D3DSURFACE_DESC description{};
  backbuffer->GetDesc(&description);
  if (description.Format != D3DFMT_A8R8G8B8 && description.Format != D3DFMT_X8R8G8B8) {
    backbuffer->Release();
    throw std::runtime_error("unsupported backbuffer format");
  }

  IDirect3DSurface9* source = backbuffer;
  IDirect3DSurface9* resolved = nullptr;
  if (description.MultiSampleType != D3DMULTISAMPLE_NONE) {
    if (FAILED(device->CreateRenderTarget(description.Width, description.Height, description.Format,
        D3DMULTISAMPLE_NONE, 0, FALSE, &resolved, nullptr)) ||
        FAILED(device->StretchRect(backbuffer, nullptr, resolved, nullptr, D3DTEXF_NONE))) {
      backbuffer->Release();
      if (resolved) resolved->Release();
      throw std::runtime_error("could not resolve multisampled backbuffer");
    }
    source = resolved;
  }

  IDirect3DSurface9* readable = nullptr;
  if (FAILED(device->CreateOffscreenPlainSurface(description.Width, description.Height,
      description.Format, D3DPOOL_SYSTEMMEM, &readable, nullptr)) ||
      FAILED(device->GetRenderTargetData(source, readable))) {
    if (readable) readable->Release();
    if (resolved) resolved->Release();
    backbuffer->Release();
    throw std::runtime_error("could not copy backbuffer to system memory");
  }
  D3DLOCKED_RECT locked{};
  if (FAILED(readable->LockRect(&locked, nullptr, D3DLOCK_READONLY))) {
    readable->Release();
    if (resolved) resolved->Release();
    backbuffer->Release();
    throw std::runtime_error("could not lock captured backbuffer");
  }

  IStream* stream = nullptr;
  IWICImagingFactory* factory = nullptr;
  IWICBitmapEncoder* encoder = nullptr;
  IWICBitmapFrameEncode* frame = nullptr;
  IPropertyBag2* properties = nullptr;
  HGLOBAL global = GlobalAlloc(GMEM_MOVEABLE, 0);
  HRESULT result = CreateStreamOnHGlobal(global, TRUE, &stream);
  if (SUCCEEDED(result)) result = CoCreateInstance(CLSID_WICImagingFactory, nullptr,
    CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&factory));
  if (SUCCEEDED(result)) result = factory->CreateEncoder(GUID_ContainerFormatPng, nullptr, &encoder);
  if (SUCCEEDED(result)) result = encoder->Initialize(stream, WICBitmapEncoderNoCache);
  if (SUCCEEDED(result)) result = encoder->CreateNewFrame(&frame, &properties);
  if (SUCCEEDED(result)) result = frame->Initialize(properties);
  if (SUCCEEDED(result)) result = frame->SetSize(description.Width, description.Height);
  auto pixelFormat = GUID_WICPixelFormat32bppBGRA;
  if (SUCCEEDED(result)) result = frame->SetPixelFormat(&pixelFormat);
  if (SUCCEEDED(result)) result = frame->WritePixels(description.Height,
    static_cast<UINT>(locked.Pitch), static_cast<UINT>(locked.Pitch * description.Height),
    static_cast<BYTE*>(locked.pBits));
  if (SUCCEEDED(result)) result = frame->Commit();
  if (SUCCEEDED(result)) result = encoder->Commit();

  std::vector<uint8_t> output;
  if (SUCCEEDED(result)) {
    STATSTG statistics{};
    result = stream->Stat(&statistics, STATFLAG_NONAME);
    if (SUCCEEDED(result) && statistics.cbSize.QuadPart <= 128 * 1024 * 1024) {
      output.resize(static_cast<size_t>(statistics.cbSize.QuadPart));
      LARGE_INTEGER start{};
      stream->Seek(start, STREAM_SEEK_SET, nullptr);
      ULONG read = 0;
      result = stream->Read(output.data(), static_cast<ULONG>(output.size()), &read);
      if (FAILED(result) || read != output.size()) output.clear();
    }
  }

  if (properties) properties->Release();
  if (frame) frame->Release();
  if (encoder) encoder->Release();
  if (factory) factory->Release();
  if (stream) stream->Release();
  readable->UnlockRect();
  readable->Release();
  if (resolved) resolved->Release();
  backbuffer->Release();
  if (output.empty()) throw std::runtime_error("WIC PNG encoding failed");
  return output;
}

HRESULT STDMETHODCALLTYPE HookPresent(
  IDirect3DDevice9* device,
  const RECT* source,
  const RECT* destination,
  HWND window,
  const RGNDATA* dirty
) {
  if (captureRequested.exchange(false)) {
    std::scoped_lock lock(stateMutex);
    capturedPng.clear();
    captureError.clear();
    try {
      const auto initialized = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
      capturedPng = EncodePng(device);
      if (SUCCEEDED(initialized)) CoUninitialize();
    } catch (const std::exception& error) {
      captureError = error.what();
    }
    SetEvent(captureComplete);
  }
  return originalPresent(device, source, destination, window, dirty);
}

HRESULT STDMETHODCALLTYPE HookSetTransform(
  IDirect3DDevice9* device,
  const D3DTRANSFORMSTATETYPE state,
  const D3DMATRIX* matrix
) {
  if (matrix && (state == D3DTS_VIEW || state == D3DTS_PROJECTION)) {
    std::scoped_lock lock(stateMutex);
    if (state == D3DTS_VIEW) {
      viewMatrix = *matrix;
      hasView = true;
    } else {
      projectionMatrix = *matrix;
      hasProjection = true;
    }
  }
  return originalSetTransform(device, state, matrix);
}

HRESULT STDMETHODCALLTYPE HookSetVertexShaderConstantF(
  IDirect3DDevice9* device,
  const UINT startRegister,
  const float* constants,
  const UINT vectorCount
) {
  if (constants && vectorCount) {
    std::scoped_lock lock(stateMutex);
    hasVertexShaderConstants = true;
    lastVertexShaderStartRegister = startRegister;
    lastVertexShaderVectorCount = vectorCount;
    const auto count = (std::min)(lastVertexShaderConstants.size(),
      static_cast<size_t>(vectorCount) * 4);
    std::copy_n(constants, count, lastVertexShaderConstants.begin());
    std::fill(lastVertexShaderConstants.begin() + count,
      lastVertexShaderConstants.end(), 0.0f);
  }
  return originalSetVertexShaderConstantF(device, startRegister, constants, vectorCount);
}

void HookDevice(IDirect3DDevice9* device) {
  auto table = *reinterpret_cast<void***>(device);
  PatchPointer(&table[17], reinterpret_cast<void*>(&HookPresent),
    reinterpret_cast<void**>(&originalPresent));
  PatchPointer(&table[44], reinterpret_cast<void*>(&HookSetTransform),
    reinterpret_cast<void**>(&originalSetTransform));
  PatchPointer(&table[94], reinterpret_cast<void*>(&HookSetVertexShaderConstantF),
    reinterpret_cast<void**>(&originalSetVertexShaderConstantF));
  hasDevice = true;
}

HRESULT STDMETHODCALLTYPE HookCreateDevice(
  IDirect3D9* direct3D,
  const UINT adapter,
  const D3DDEVTYPE deviceType,
  const HWND window,
  const DWORD behavior,
  D3DPRESENT_PARAMETERS* parameters,
  IDirect3DDevice9** device
) {
  const auto result = originalCreateDevice(direct3D, adapter, deviceType, window, behavior,
    parameters, device);
  if (SUCCEEDED(result) && device && *device) HookDevice(*device);
  if (SUCCEEDED(result) && window) deviceWindow = window;
  return result;
}

IDirect3D9* WINAPI HookDirect3DCreate9(const UINT sdkVersion) {
  auto direct3D = originalDirect3DCreate9(sdkVersion);
  if (direct3D) {
    auto table = *reinterpret_cast<void***>(direct3D);
    PatchPointer(&table[16], reinterpret_cast<void*>(&HookCreateDevice),
      reinterpret_cast<void**>(&originalCreateDevice));
  }
  return direct3D;
}

bool InstallImportHook() {
  auto module = reinterpret_cast<uint8_t*>(GetModuleHandleW(nullptr));
  const auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(module);
  if (!module || dos->e_magic != IMAGE_DOS_SIGNATURE) return false;
  const auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(module + dos->e_lfanew);
  if (nt->Signature != IMAGE_NT_SIGNATURE) return false;
  const auto directory = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT];
  if (!directory.VirtualAddress) return false;
  auto import = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(module + directory.VirtualAddress);
  for (; import->Name; ++import) {
    const auto name = reinterpret_cast<const char*>(module + import->Name);
    if (_stricmp(name, "d3d9.dll") != 0) continue;
    auto thunk = reinterpret_cast<IMAGE_THUNK_DATA*>(module + import->FirstThunk);
    auto originalThunk = import->OriginalFirstThunk
      ? reinterpret_cast<IMAGE_THUNK_DATA*>(module + import->OriginalFirstThunk)
      : thunk;
    for (; originalThunk->u1.AddressOfData; ++originalThunk, ++thunk) {
      if (IMAGE_SNAP_BY_ORDINAL(originalThunk->u1.Ordinal)) continue;
      const auto imported = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(
        module + originalThunk->u1.AddressOfData);
      if (std::strcmp(reinterpret_cast<const char*>(imported->Name), "Direct3DCreate9") != 0)
        continue;
      return PatchPointer(reinterpret_cast<void**>(&thunk->u1.Function),
        reinterpret_cast<void*>(&HookDirect3DCreate9),
        reinterpret_cast<void**>(&originalDirect3DCreate9));
    }
  }
  return false;
}

std::string MatrixJson(const D3DMATRIX& matrix) {
  const auto values = reinterpret_cast<const float*>(&matrix);
  std::ostringstream output;
  output.precision(9);
  output << '[';
  for (size_t index = 0; index < 16; ++index) {
    if (index) output << ',';
    output << values[index];
  }
  output << ']';
  return output.str();
}

std::string StateJson() {
  std::scoped_lock lock(stateMutex);
  std::ostringstream output;
  RECT client{};
  const auto window = deviceWindow.load();
  const auto hasWindow = window && IsWindow(window) && GetClientRect(window, &client);
  output << "{\"device_ready\":" << (hasDevice ? "true" : "false")
    << ",\"window_ready\":" << (hasWindow ? "true" : "false")
    << ",\"client_width\":" << (hasWindow ? client.right - client.left : 0)
    << ",\"client_height\":" << (hasWindow ? client.bottom - client.top : 0)
    << ",\"view_observed\":" << (hasView ? "true" : "false")
    << ",\"projection_observed\":" << (hasProjection ? "true" : "false")
    << ",\"view\":" << (hasView ? MatrixJson(viewMatrix) : "null")
    << ",\"projection\":" << (hasProjection ? MatrixJson(projectionMatrix) : "null")
    << ",\"vertex_shader_constants_observed\":"
    << (hasVertexShaderConstants ? "true" : "false")
    << ",\"last_vertex_shader_start_register\":" << lastVertexShaderStartRegister
    << ",\"last_vertex_shader_vector_count\":" << lastVertexShaderVectorCount
    << ",\"last_vertex_shader_constants\":";
  if (!hasVertexShaderConstants) {
    output << "null";
  } else {
    output << '[';
    const auto count = (std::min)(lastVertexShaderConstants.size(),
      static_cast<size_t>(lastVertexShaderVectorCount) * 4);
    for (size_t index = 0; index < count; ++index) {
      if (index) output << ',';
      output << lastVertexShaderConstants[index];
    }
    output << ']';
  }
  output << '}';
  return output.str();
}

std::string ClickJson(const rct3bridge::Request& request) {
  const auto window = deviceWindow.load();
  DWORD processId = 0;
  if (!window || !IsWindow(window) ||
      !GetWindowThreadProcessId(window, &processId) || processId != GetCurrentProcessId())
    return "{\"error\":{\"code\":\"window_not_ready\",\"message\":\"Retail window is not ready\"}}";
  if (!request.x || !request.y)
    return "{\"error\":{\"code\":\"invalid_coordinates\",\"message\":\"Click requires x and y\"}}";
  RECT client{};
  if (!GetClientRect(window, &client) || *request.x < 0 || *request.y < 0 ||
      *request.x >= client.right - client.left || *request.y >= client.bottom - client.top)
    return "{\"error\":{\"code\":\"invalid_coordinates\",\"message\":\"Click is outside the retail client area\"}}";
  const auto point = MAKELPARAM(*request.x, *request.y);
  if (!PostMessageW(window, WM_MOUSEMOVE, 0, point) ||
      !PostMessageW(window, WM_LBUTTONDOWN, MK_LBUTTON, point) ||
      !PostMessageW(window, WM_LBUTTONUP, 0, point))
    return "{\"error\":{\"code\":\"click_failed\",\"message\":\"Could not post retail click\"}}";
  return "{\"accepted\":true}";
}

std::wstring Environment(const wchar_t* name) {
  const auto length = GetEnvironmentVariableW(name, nullptr, 0);
  if (!length) return {};
  std::wstring output(length - 1, L'\0');
  GetEnvironmentVariableW(name, output.data(), length);
  return output;
}

std::string NarrowAscii(const std::wstring& value) {
  std::string output;
  output.reserve(value.size());
  for (const auto character : value) {
    if (character < 33 || character > 126) return {};
    output.push_back(static_cast<char>(character));
  }
  return output;
}

bool ReadMessage(const HANDLE pipe, std::string& message) {
  message.clear();
  char value = 0;
  DWORD read = 0;
  while (message.size() <= rct3bridge::MaxMessageBytes) {
    if (!ReadFile(pipe, &value, 1, &read, nullptr) || read != 1) return false;
    if (value == '\n') return true;
    if (value != '\r') message.push_back(value);
  }
  return false;
}

bool WriteMessage(const HANDLE pipe, const std::string& message) {
  const auto line = message + "\n";
  DWORD written = 0;
  return WriteFile(pipe, line.data(), static_cast<DWORD>(line.size()), &written, nullptr) &&
    written == line.size();
}

DWORD WINAPI BridgeThread(void*) {
  const auto pipeToken = NarrowAscii(Environment(L"RCT3BRIDGE_PIPE"));
  const auto nonce = NarrowAscii(Environment(L"RCT3BRIDGE_NONCE"));
  if (pipeToken.empty() || nonce.size() < 32 || nonce.size() > 128 || !InstallImportHook()) return 1;
  captureComplete = CreateEventW(nullptr, TRUE, FALSE, nullptr);
  if (!captureComplete) return 1;

  PSECURITY_DESCRIPTOR descriptor = nullptr;
  if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(
      L"D:P(A;;GA;;;OW)", SDDL_REVISION_1, &descriptor, nullptr)) return 1;
  SECURITY_ATTRIBUTES security{sizeof(security), descriptor, FALSE};
  const auto pipeName = L"\\\\.\\pipe\\" + std::wstring(pipeToken.begin(), pipeToken.end());
  const auto pipe = CreateNamedPipeW(pipeName.c_str(), PIPE_ACCESS_DUPLEX,
    PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS, 1,
    1024 * 1024, 64 * 1024, 0, &security);
  LocalFree(descriptor);
  if (pipe == INVALID_HANDLE_VALUE) return 1;
  if (!ConnectNamedPipe(pipe, nullptr) && GetLastError() != ERROR_PIPE_CONNECTED) {
    CloseHandle(pipe);
    return 1;
  }

  bool running = true;
  std::string input;
  while (running && ReadMessage(pipe, input)) {
    std::string validationError;
    const auto request = rct3bridge::ParseRequest(input, nonce, validationError);
    if (!request) {
      WriteMessage(pipe, "{\"id\":null,\"error\":{\"code\":\"invalid_request\",\"message\":\"" +
        rct3bridge::EscapeJson(validationError) + "\"}}");
      break;
    }
    std::string result;
    if (request->method == "hello" || request->method == "state") {
      result = StateJson();
    } else if (request->method == "capture") {
      if (!hasDevice) {
        result = "{\"error\":{\"code\":\"device_not_ready\",\"message\":\"D3D9 device is not ready\"}}";
      } else {
        ResetEvent(captureComplete);
        captureRequested = true;
        if (WaitForSingleObject(captureComplete, 15000) != WAIT_OBJECT_0) {
          captureRequested = false;
          result = "{\"error\":{\"code\":\"capture_timeout\",\"message\":\"Present did not complete capture\"}}";
        } else {
          std::scoped_lock lock(stateMutex);
          if (!captureError.empty()) {
            result = "{\"error\":{\"code\":\"capture_failed\",\"message\":\"" +
              rct3bridge::EscapeJson(captureError) + "\"}}";
          } else {
            result = "{\"png_base64\":\"" + Base64(capturedPng) + "\"}";
          }
        }
      }
    } else if (request->method == "click") {
      result = ClickJson(*request);
    } else if (request->method == "shutdown") {
      result = "{\"accepted\":true}";
      running = false;
    } else {
      result = "{\"error\":{\"code\":\"unknown_method\",\"message\":\"Unknown method\"}}";
    }

    if (result.rfind("{\"error\"", 0) == 0) {
      WriteMessage(pipe, "{\"id\":\"" + rct3bridge::EscapeJson(request->id) + "\"," +
        result.substr(1));
    } else {
      WriteMessage(pipe, "{\"id\":\"" + rct3bridge::EscapeJson(request->id) +
        "\",\"result\":" + result + '}');
    }
  }
  FlushFileBuffers(pipe);
  DisconnectNamedPipe(pipe);
  CloseHandle(pipe);
  CloseHandle(captureComplete);
  return 0;
}

}

BOOL WINAPI DllMain(const HINSTANCE instance, const DWORD reason, void*) {
  if (reason != DLL_PROCESS_ATTACH) return TRUE;
  DisableThreadLibraryCalls(instance);
  const auto thread = CreateThread(nullptr, 0, BridgeThread, nullptr, 0, nullptr);
  if (thread) CloseHandle(thread);
  return thread != nullptr;
}
