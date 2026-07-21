#include "BridgeProtocol.h"

#include <cstdlib>
#include <iostream>
#include <string>

namespace {

void Require(const bool condition, const char* message) {
  if (condition) return;
  std::cerr << message << '\n';
  std::exit(1);
}

}

int main() {
  std::string error;
  const auto valid = rct3bridge::ParseRequest(
    R"({"version":1,"nonce":"abc","id":"7","method":"capture"})",
    "abc",
    error);
  Require(valid.has_value(), "valid request was rejected");
  Require(valid->id == "7" && valid->method == "capture", "valid request parsed incorrectly");

  const auto click = rct3bridge::ParseRequest(
    R"({"version":1,"nonce":"abc","id":"8","method":"click","x":640,"y":360})",
    "abc",
    error);
  Require(click.has_value() && click->x == 640 && click->y == 360,
    "click coordinates parsed incorrectly");

  Require(!rct3bridge::ParseRequest(
    R"({"version":2,"nonce":"abc","id":"7","method":"state"})", "abc", error),
    "wrong protocol version was accepted");
  Require(!rct3bridge::ParseRequest(
    R"({"version":1,"nonce":"wrong","id":"7","method":"state"})", "abc", error),
    "wrong nonce was accepted");
  Require(!rct3bridge::ParseRequest(
    R"({"version":1,"nonce":"abc","id":"7","method":"state","extra":"x"})",
    "abc",
    error),
    "unknown property was accepted");
  Require(!rct3bridge::ParseRequest(
    R"({"version":1,"nonce":"abc","id":"7","id":"8","method":"state"})",
    "abc",
    error),
    "duplicate property was accepted");
  Require(!rct3bridge::ParseRequest(std::string(rct3bridge::MaxMessageBytes + 1, 'x'),
    "abc", error), "oversized request was accepted");
  return 0;
}
