#include "BridgeProtocol.h"

#include <cctype>
#include <limits>

namespace rct3bridge {
namespace {

void SkipSpace(std::string_view input, size_t& offset) {
  while (offset < input.size() && std::isspace(static_cast<unsigned char>(input[offset])))
    ++offset;
}

bool ParseString(std::string_view input, size_t& offset, std::string& output) {
  SkipSpace(input, offset);
  if (offset >= input.size() || input[offset++] != '"') return false;
  output.clear();
  while (offset < input.size()) {
    const auto value = input[offset++];
    if (value == '"') return true;
    if (value == '\\' || static_cast<unsigned char>(value) < 32) return false;
    output.push_back(value);
  }
  return false;
}

bool ParseInteger(std::string_view input, size_t& offset, int& output) {
  SkipSpace(input, offset);
  if (offset >= input.size() || !std::isdigit(static_cast<unsigned char>(input[offset])))
    return false;
  output = 0;
  while (offset < input.size() && std::isdigit(static_cast<unsigned char>(input[offset]))) {
    const auto digit = input[offset++] - '0';
    if (output > (std::numeric_limits<int>::max() - digit) / 10) return false;
    output = output * 10 + digit;
  }
  return true;
}

}

std::optional<Request> ParseRequest(
  const std::string_view input,
  const std::string_view expectedNonce,
  std::string& error
) {
  if (input.empty() || input.size() > MaxMessageBytes) {
    error = "invalid message length";
    return std::nullopt;
  }

  size_t offset = 0;
  SkipSpace(input, offset);
  if (offset >= input.size() || input[offset++] != '{') {
    error = "request must be an object";
    return std::nullopt;
  }

  std::string id;
  std::string method;
  std::string nonce;
  int version = -1;
  std::optional<int> x;
  std::optional<int> y;
  bool hasVersion = false;
  bool hasNonce = false;
  bool hasId = false;
  bool hasMethod = false;
  bool first = true;
  while (true) {
    SkipSpace(input, offset);
    if (offset < input.size() && input[offset] == '}') {
      ++offset;
      break;
    }
    if (!first) {
      if (offset >= input.size() || input[offset++] != ',') {
        error = "invalid object separator";
        return std::nullopt;
      }
      SkipSpace(input, offset);
    }
    first = false;

    std::string key;
    if (!ParseString(input, offset, key)) {
      error = "invalid property name";
      return std::nullopt;
    }
    SkipSpace(input, offset);
    if (offset >= input.size() || input[offset++] != ':') {
      error = "missing property separator";
      return std::nullopt;
    }
    if (key == "version") {
      if (hasVersion || !ParseInteger(input, offset, version)) {
        error = "invalid version";
        return std::nullopt;
      }
      hasVersion = true;
    } else if (key == "nonce") {
      if (hasNonce || !ParseString(input, offset, nonce)) {
        error = "invalid nonce";
        return std::nullopt;
      }
      hasNonce = true;
    } else if (key == "id") {
      if (hasId || !ParseString(input, offset, id)) {
        error = "invalid id";
        return std::nullopt;
      }
      hasId = true;
    } else if (key == "method") {
      if (hasMethod || !ParseString(input, offset, method)) {
        error = "invalid method";
        return std::nullopt;
      }
      hasMethod = true;
    } else if (key == "x") {
      int value = 0;
      if (x || !ParseInteger(input, offset, value)) {
        error = "invalid x coordinate";
        return std::nullopt;
      }
      x = value;
    } else if (key == "y") {
      int value = 0;
      if (y || !ParseInteger(input, offset, value)) {
        error = "invalid y coordinate";
        return std::nullopt;
      }
      y = value;
    } else {
      error = "unknown property";
      return std::nullopt;
    }
  }
  SkipSpace(input, offset);
  if (offset != input.size() || version != ProtocolVersion || nonce != expectedNonce ||
      id.empty() || id.size() > 64 || method.empty() || method.size() > 32) {
    error = "protocol validation failed";
    return std::nullopt;
  }
  return Request{id, method, x, y};
}

std::string EscapeJson(const std::string_view value) {
  std::string output;
  output.reserve(value.size());
  for (const auto character : value) {
    if (character == '"' || character == '\\') output.push_back('\\');
    if (character == '\n') {
      output += "\\n";
    } else if (character == '\r') {
      output += "\\r";
    } else if (static_cast<unsigned char>(character) >= 32) {
      output.push_back(character);
    }
  }
  return output;
}

}
