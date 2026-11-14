"""
Rewrites minimal-API handlers to take [AsParameters] records (readability plan, phase 5): a handler's parameter
list replaced, and `var (a, _, c) = record;` lines put first in its body so that the body keeps its names (the
names it does not use become `_`). Used from a small script per endpoints file; see README.md.

    from handler_params import Source, services_record
    text = Source("src/Sfe.Api/Endpoints/TextEndpoints.cs")
    lines = [text.deconstruct("Get", "services", ["content", "trash", "paths"])]
    text.rewrite("Get", ["[AsParameters] TextServices services", "string? path"], lines)
    text.save()
"""
import re


class Source:
    def __init__(self, path):
        self.path = path
        self.s = open(path, encoding='utf-8', newline='').read()

    def save(self):
        open(self.path, 'w', encoding='utf-8', newline='').write(self.s)

    def _match(self, name):
        m = re.search(r'\n[^\n]*\b' + re.escape(name) + r'\(\n((?:        [^\n]*\n)+?)    \)\n    \{\n', self.s)
        assert m, f'{self.path}: {name}'
        return m

    def params(self, name):
        return [p.strip().rstrip(',') for p in self._match(name).group(1).strip().split('\n')]

    def body(self, name):
        m = self._match(name)
        end = self.s.index('\n    }\n', m.end())
        return self.s[m.end():end]

    def deconstruct(self, name, record_var, fields):
        """`var (a, _, c) = record_var;` with the fields the handler's body uses, the others discarded."""
        body = self.body(name)
        used = [f if re.search(r'\b' + re.escape(f) + r'\b', body) else '_' for f in fields]
        if all(u == '_' for u in used):
            return None
        return f'var ({", ".join(used)}) = {record_var};'

    def rewrite(self, name, new_params, first_lines):
        m = self._match(name)
        params = ',\n'.join('        ' + p for p in new_params) + '\n'
        head = m.group(0).replace(m.group(1), params)
        lines = ''.join('        ' + line + '\n' for line in first_lines if line)
        self.s = self.s[:m.start()] + head + lines + self.s[m.end():]


def services_record(namespace_usings, name, summary, fields):
    """The text of a services record file: fields as (Type, Name)."""
    usings = ''.join(f'using {u};\n' for u in namespace_usings)
    body = ',\n'.join(f'    {t} {n}' for t, n in fields)
    return (f'{usings}\nnamespace Otools.Net.Sfe.Api.Endpoints;\n\n/// <summary>\n/// {summary}\n/// </summary>\n'
            f'internal sealed record {name}(\n{body}\n);\n')
