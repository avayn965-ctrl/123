"""Check variables, formatter calls, conditions and markup in both languages.

Russian uses number-neutral phrases instead of English plural formatters. This
also avoids depending on the base game's language to select Russian plurals.
"""
from collections import Counter
import json
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def inspect(text):
    variables, calls, conditions, prose = set(), Counter(), Counter(), []

    def scan(value):
        i, literal = 0, []
        while i < len(value):
            if value[i] == '\\' and i + 1 < len(value):
                literal.append(value[i:i+2]); i += 2; continue
            if value[i] == '}':
                raise ValueError('unmatched closing brace')
            if value[i] != '{':
                literal.append(value[i]); i += 1; continue
            prose.append(''.join(literal)); literal = []
            start, depth = i + 1, 1
            i += 1
            while i < len(value) and depth:
                if value[i] == '\\': i += 2; continue
                if value[i] == '{': depth += 1
                elif value[i] == '}': depth -= 1
                i += 1
            if depth: raise ValueError('unclosed placeholder')
            inner = value[start:i-1]
            name, colon, rest = inner.partition(':')
            if not re.fullmatch(r'[A-Za-z0-9_.]*', name):
                raise ValueError('invalid variable: ' + name)
            if name: variables.add(name)
            if not colon: continue
            if re.fullmatch(r'\w+\([^{}]*\)', rest):
                calls[(name, rest)] += 1; continue
            formatter = ''
            if rest.startswith(('show:', 'cond:', 'plural:')):
                formatter, rest = rest.split(':', 1)
            # Preserve each non-plural selector's identity and branch count.
            depth2, branches = 0, 1
            for c in rest:
                if c == '{': depth2 += 1
                elif c == '}': depth2 -= 1
                elif c == '|' and depth2 == 0: branches += 1
            if formatter != 'plural': conditions[(name, formatter, branches)] += 1
            scan(rest)
        prose.append(''.join(literal))
    scan(text)
    return variables, calls, conditions, ''.join(prose)


def validate():
    errors, count = [], 0
    eng, rus = ROOT/'localization/eng', ROOT/'localization/rus'
    assert {x.name for x in eng.glob('*.json')} == {x.name for x in rus.glob('*.json')}
    for file in sorted(eng.glob('*.json')):
        original = json.loads(file.read_text(encoding='utf-8-sig'))
        target = json.loads((rus/file.name).read_text())
        assert original.keys() == target.keys(), file.name + ': changed keys'
        for key, text in original.items():
            count += 1
            try:
                translated = target[key]
                assert bool(text.strip()) == bool(translated.strip()), 'missing/added text'
                a, b = inspect(text), inspect(translated)
                assert a[:3] == b[:3], f'format signature mismatch: {a[:3]} != {b[:3]}'
                assert Counter(re.findall(r'\[/?[a-z]+\]', text)) == Counter(re.findall(r'\[/?[a-z]+\]', translated)), 'markup mismatch'
                assert not re.search(r'ZXQ|[\u4e00-\u9fff]', translated), 'untranslated marker/text'
                assert ':plural:' not in translated, 'English plural rules remain'
                visible = re.sub(r'\[/?[a-z]+\]', '', b[3])
                visible = re.sub(r'Ruina|Mk\.', '', visible)
                assert not re.search(r'[A-Za-z]{2,}', visible), 'English visible text: ' + visible
            except (AssertionError, ValueError) as error:
                errors.append(f'{file.name}:{key}: {error}')
    assert count == 1418, count
    if errors: raise ValueError('\n'.join(errors))
    print(f'PASS: {count} entries / 22 tables; keys, variables, formatters, conditions and markup checked.')
    return count


if __name__ == '__main__': validate()
