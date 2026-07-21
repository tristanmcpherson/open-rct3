#include <windows.h>
#include <wincrypt.h>

#include <array>
#include <filesystem>
#include <iomanip>
#include <iostream>
#include <sstream>
#include <string>
#include <vector>

namespace {

std::wstring Quote(const std::wstring& value) {
  std::wstring output = L"\"";
  size_t slashes = 0;
  for (const auto character : value) {
    if (character == L'\\') {
      ++slashes;
    } else if (character == L'"') {
      output.append(slashes * 2 + 1, L'\\');
      output.push_back(character);
      slashes = 0;
    } else {
      output.append(slashes, L'\\');
      slashes = 0;
      output.push_back(character);
    }
  }
  output.append(slashes * 2, L'\\');
  output.push_back(L'"');
  return output;
}

std::string Sha256(const std::filesystem::path& path) {
  HCRYPTPROV provider = 0;
  HCRYPTHASH hash = 0;
  if (!CryptAcquireContextW(&provider, nullptr, nullptr, PROV_RSA_AES, CRYPT_VERIFYCONTEXT) ||
      !CryptCreateHash(provider, CALG_SHA_256, 0, 0, &hash))
    throw std::runtime_error("could not initialize SHA-256");

  auto file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
    FILE_ATTRIBUTE_NORMAL, nullptr);
  if (file == INVALID_HANDLE_VALUE) throw std::runtime_error("could not open retail executable");
  std::array<BYTE, 64 * 1024> buffer{};
  DWORD read = 0;
  while (ReadFile(file, buffer.data(), static_cast<DWORD>(buffer.size()), &read, nullptr) && read) {
    if (!CryptHashData(hash, buffer.data(), read, 0)) {
      CloseHandle(file);
      throw std::runtime_error("could not hash retail executable");
    }
  }
  CloseHandle(file);

  std::array<BYTE, 32> digest{};
  DWORD length = static_cast<DWORD>(digest.size());
  if (!CryptGetHashParam(hash, HP_HASHVAL, digest.data(), &length, 0))
    throw std::runtime_error("could not finish retail executable hash");
  CryptDestroyHash(hash);
  CryptReleaseContext(provider, 0);

  std::ostringstream output;
  output << std::hex << std::setfill('0');
  for (const auto value : digest) output << std::setw(2) << static_cast<int>(value);
  return output.str();
}

void VerifyX86(const std::filesystem::path& path) {
  auto file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ, nullptr, OPEN_EXISTING,
    FILE_ATTRIBUTE_NORMAL, nullptr);
  if (file == INVALID_HANDLE_VALUE) throw std::runtime_error("could not inspect retail executable");
  IMAGE_DOS_HEADER dos{};
  DWORD read = 0;
  if (!ReadFile(file, &dos, sizeof(dos), &read, nullptr) || read != sizeof(dos) ||
      dos.e_magic != IMAGE_DOS_SIGNATURE)
    throw std::runtime_error("retail executable has an invalid DOS header");
  LARGE_INTEGER offset{};
  offset.QuadPart = dos.e_lfanew;
  SetFilePointerEx(file, offset, nullptr, FILE_BEGIN);
  DWORD signature = 0;
  IMAGE_FILE_HEADER header{};
  const auto valid = ReadFile(file, &signature, sizeof(signature), &read, nullptr) &&
    read == sizeof(signature) && signature == IMAGE_NT_SIGNATURE &&
    ReadFile(file, &header, sizeof(header), &read, nullptr) && read == sizeof(header) &&
    header.Machine == IMAGE_FILE_MACHINE_I386;
  CloseHandle(file);
  if (!valid) throw std::runtime_error("retail executable is not the pinned 32-bit PE target");
}

void Inject(const PROCESS_INFORMATION& child, const std::filesystem::path& libraryPath) {
  const auto localKernel32 = reinterpret_cast<uintptr_t>(GetModuleHandleW(L"kernel32.dll"));
  const auto localLoadLibrary = reinterpret_cast<uintptr_t>(
    GetProcAddress(reinterpret_cast<HMODULE>(localKernel32), "LoadLibraryW"));
  if (!localLoadLibrary) throw std::runtime_error("could not resolve LoadLibraryW");

  const auto absolute = std::filesystem::absolute(libraryPath).wstring();
  const auto bytes = (absolute.size() + 1) * sizeof(wchar_t);
  auto remotePath = VirtualAllocEx(child.hProcess, nullptr, bytes, MEM_COMMIT | MEM_RESERVE,
    PAGE_READWRITE);
  if (!remotePath) throw std::runtime_error("could not allocate child library path");
  if (!WriteProcessMemory(child.hProcess, remotePath, absolute.c_str(), bytes, nullptr)) {
    VirtualFreeEx(child.hProcess, remotePath, 0, MEM_RELEASE);
    throw std::runtime_error("could not write child library path");
  }
  auto thread = CreateRemoteThread(child.hProcess, nullptr, 0,
    reinterpret_cast<LPTHREAD_START_ROUTINE>(localLoadLibrary), remotePath, 0, nullptr);
  if (!thread) {
    VirtualFreeEx(child.hProcess, remotePath, 0, MEM_RELEASE);
    throw std::runtime_error("could not start LoadLibraryW in child");
  }
  const auto wait = WaitForSingleObject(thread, 15000);
  DWORD loadedModule = 0;
  if (wait != WAIT_OBJECT_0 || !GetExitCodeThread(thread, &loadedModule) || !loadedModule) {
    CloseHandle(thread);
    VirtualFreeEx(child.hProcess, remotePath, 0, MEM_RELEASE);
    throw std::runtime_error("child LoadLibraryW failed or timed out");
  }
  CloseHandle(thread);
  VirtualFreeEx(child.hProcess, remotePath, 0, MEM_RELEASE);
}

}

int wmain(const int argc, wchar_t* argv[]) {
  if (argc != 4) {
    std::cerr << "usage: RCT3Launcher.exe <RCT3.exe> <RCT3Bridge.dll> <sha256>\n";
    return 2;
  }
  PROCESS_INFORMATION child{};
  try {
    const auto executable = std::filesystem::absolute(argv[1]);
    const auto library = std::filesystem::absolute(argv[2]);
    if (!std::filesystem::is_regular_file(executable) || !std::filesystem::is_regular_file(library))
      throw std::runtime_error("retail executable or bridge library is missing");
    VerifyX86(executable);
    if (_stricmp(Sha256(executable).c_str(), std::filesystem::path(argv[3]).string().c_str()) != 0)
      throw std::runtime_error("retail executable SHA-256 does not match the configured pin");

    STARTUPINFOW startup{sizeof(startup)};
    auto command = Quote(executable.wstring());
    std::vector<wchar_t> commandBuffer(command.begin(), command.end());
    commandBuffer.push_back(L'\0');
    if (!CreateProcessW(executable.c_str(), commandBuffer.data(), nullptr, nullptr, FALSE,
        CREATE_SUSPENDED, nullptr, executable.parent_path().c_str(), &startup, &child))
      throw std::runtime_error("CreateProcessW failed");
    Inject(child, library);
    if (ResumeThread(child.hThread) == static_cast<DWORD>(-1))
      throw std::runtime_error("could not resume retail process");

    FILETIME created{}, exited{}, kernel{}, user{};
    if (!GetProcessTimes(child.hProcess, &created, &exited, &kernel, &user))
      throw std::runtime_error("could not read retail process creation time");
    ULARGE_INTEGER creation{};
    creation.LowPart = created.dwLowDateTime;
    creation.HighPart = created.dwHighDateTime;
    std::cout << "{\"pid\":" << child.dwProcessId << ",\"creation_filetime\":"
      << creation.QuadPart << "}" << std::endl;
    CloseHandle(child.hThread);
    CloseHandle(child.hProcess);
    return 0;
  } catch (const std::exception& error) {
    if (child.hProcess) TerminateProcess(child.hProcess, 1);
    if (child.hThread) CloseHandle(child.hThread);
    if (child.hProcess) CloseHandle(child.hProcess);
    std::cerr << error.what() << '\n';
    return 1;
  }
}
