#include "surface.hpp"

namespace sp {

std::vector<Seg> subtract(double a, double b, const std::vector<Seg>& cut) {
    std::vector<Seg> res{{a, b}};
    for (auto& c : cut) {
        std::vector<Seg> next;
        for (auto& s : res) {
            if (c.b <= s.a || c.a >= s.b) { next.push_back(s); continue; }
            if (c.a > s.a) next.push_back({s.a, c.a});
            if (c.b < s.b) next.push_back({c.b, s.b});
        }
        res = std::move(next);
        if (res.empty()) break;
    }
    return res;
}

}  // namespace sp
