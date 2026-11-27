// Простой запрос по https (WinHTTP): коротко, с лимитами. Нужен для каталога, погоды, правил панели и отчётов.
#pragma once
#include <string>

namespace gs {

// method: "GET" или "PUT"/"POST" (тело и тип содержимого — для них). Ответ — в *body или в файл *file (что-то одно). true — ответ 200–299.
bool httpFetch(const char* method, const std::string& url, const std::string& reqBody, const std::string& contentType,
               std::string* body, const std::wstring* file, long long maxBytes, std::string& error);

}  // namespace gs
