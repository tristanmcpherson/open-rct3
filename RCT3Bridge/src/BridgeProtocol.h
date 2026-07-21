#pragma once

#include <optional>
#include <string>
#include <string_view>

namespace rct3bridge {

constexpr size_t MaxMessageBytes = 64 * 1024;
constexpr int ProtocolVersion = 1;

struct Request {
  std::string id;
  std::string method;
  std::optional<int> x;
  std::optional<int> y;
};

std::optional<Request> ParseRequest(
  std::string_view input,
  std::string_view expectedNonce,
  std::string& error);
std::string EscapeJson(std::string_view value);

}
