const escape = (text: string): string => text.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');

export function createScenarioTranslator(catalogue: Record<string, string>): (text: string) => string {
  const templates = Object.entries(catalogue).filter(([source]) => /\{\w+\}/.test(source)).map(([source, translated]) => {
    const names: string[] = [];
    const pattern = source.split(/(\{\w+\})/).map(part => {
      if (!/^\{\w+\}$/.test(part)) return escape(part);
      names.push(part.slice(1, -1)); return '([^\\n]+?)';
    }).join('');
    return { pattern: new RegExp(`^${pattern}$`), names, translated };
  });
  return text => {
    if (Object.hasOwn(catalogue, text)) return catalogue[text]!;
    for (const template of templates) {
      const match = template.pattern.exec(text);
      if (!match) continue;
      const values = Object.fromEntries(template.names.map((name, index) => [name, match[index + 1]!]));
      return template.translated.replace(/\{(\w+)\}/g, (token, name: string) => values[name] ?? token);
    }
    return text;
  };
}
