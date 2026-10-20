"""Validate all tables without requiring the game or network access."""
import json
from collections import Counter
from pathlib import Path
import re

ROOT = Path(__file__).resolve().parents[1]


def signature(text):
    """Compare SmartFormat's variable/selector trees, ignoring translated prose."""
    def message(index):
        variables, selectors = Counter(), []
        while index < len(text):
            char = text[index]
            if char in '|}':
                return (tuple(sorted(variables.items())), tuple(selectors)), index
            if char != '{':
                index += 1
                continue
            head = re.match(r'\{([\w.-]+):(?:show:|cond:[^?{}]*\?)', text[index:])
            if head:
                header = head[0]
                index += len(header)
                branches = []
                while True:
                    branch, index = message(index)
                    branches.append(branch)
                    if index >= len(text):
                        raise ValueError('Unclosed selector')
                    end = text[index]
                    index += 1
                    if end == '}':
                        break
                selectors.append((header, tuple(branches)))
            else:
                end = text.index('}', index)
                token = text[index:end + 1]
                if '{' in token[1:]:
                    raise ValueError('Invalid variable: ' + token)
                variables[token] += 1
                index = end + 1
        return (tuple(sorted(variables.items())), tuple(selectors)), index
    result, index = message(0)
    if index != len(text):
        raise ValueError('Unexpected closing delimiter')
    return result


def validate():
    english, russian = ROOT / 'localization/eng', ROOT / 'localization/rus'
    assert {p.name for p in english.glob('*.json')} == {p.name for p in russian.glob('*.json')}
    count = 0
    errors = []
    for path in sorted(english.glob('*.json')):
        original = json.loads(path.read_text(encoding='utf-8-sig'))
        translated = json.loads((russian / path.name).read_text())
        assert original.keys() == translated.keys(), path.name + ': changed keys'
        for key, text in original.items():
            target = translated[key]
            count += 1
            try:
                assert isinstance(target, str)
                assert not text.strip() or target.strip(), 'missing text'
                assert signature(text) == signature(target), 'changed SmartFormat variable/selector tree'
                assert Counter(re.findall(r'\[/?[a-z]+\]', text)) == Counter(re.findall(r'\[/?[a-z]+\]', target)), 'changed BBCode tags'
                assert not re.search(r'[\u4e00-\u9fff]', target), 'untranslated Chinese text'
                assert not re.search(r'ZXQ\d+QXZ', target), 'temporary placeholder'
            except (ValueError, AssertionError) as error:
                errors.append(f'{path.name}:{key}: {error}')
    if errors:
        raise ValueError('\n'.join(errors))
    assert count == 1388, count
    print(f'PASS: {count} entries in 13 tables; all keys, SmartFormat trees, variables and BBCode preserved.')
    return count


if __name__ == '__main__':
    validate()
