/** MIT. Settings are synchronized by the embedding workbench, not read from process globals. */
export function createConfiguration(request, changed) {
  let defaults = {}, global = {}, workspace = {};
  const safeKey = key => { if (key.split('.').some(part => ['__proto__', 'prototype', 'constructor'].includes(part))) throw new Error('Unsupported configuration key.'); return key; };
  const clone = value => value === undefined ? undefined : structuredClone(value);
  const object = value => value && typeof value === 'object' && !Array.isArray(value);
  const merge = (a, b) => {
    if (!object(a) || !object(b)) return clone(b);
    const result = Object.assign(Object.create(null), a);
    for (const key of Object.keys(b)) { safeKey(key); result[key] = object(b[key]) && object(result[key]) ? merge(result[key], b[key]) : clone(b[key]); }
    return result;
  };
  function lookup(map, key) {
    safeKey(key);
    if (Object.hasOwn(map, key)) return map[key];
    const prefix = key ? key + '.' : ''; let result;
    for (const [name, value] of Object.entries(map)) if (name.startsWith(prefix)) {
      const parts = name.slice(prefix.length).split('.'); safeKey(name);
      result ??= Object.create(null); let current = result;
      for (let i = 0; i < parts.length - 1; i++) { if (!object(current[parts[i]])) current[parts[i]] = Object.create(null); current = current[parts[i]]; }
      current[parts.at(-1)] = clone(value);
    }
    return result;
  }
  function value(key) {
    let result;
    for (const layer of [defaults, global, workspace]) { const next = lookup(layer, key); if (next !== undefined) result = object(result) && object(next) ? merge(result, next) : clone(next); }
    return result;
  }
  function update(data) {
    const old = new Map(Object.keys({ ...defaults, ...global, ...workspace }).map(key => [key, JSON.stringify(value(key))]));
    defaults = clone(data.defaults ?? {}); global = clone(data.global ?? {}); workspace = clone(data.workspace ?? {});
    const keys = new Set([...old.keys(), ...Object.keys({ ...defaults, ...global, ...workspace })]);
    const different = [...keys].filter(key => old.get(key) !== JSON.stringify(value(key)));
    if (different.length) changed.fire({ affectsConfiguration: section => different.some(key => key === section || key.startsWith(section + '.') || section.startsWith(key + '.')) });
  }
  function getConfiguration(section = '', scope) {
    if (scope !== undefined && scope !== null) throw new Error('Resource/language-scoped configuration is not implemented.');
    safeKey(section);
    const key = name => safeKey(section ? section + (name ? '.' + name : '') : name);
    return {
      get(name, fallback) { const result = value(key(name)); return result === undefined ? fallback : result; },
      has(name) { return value(key(name)) !== undefined; },
      inspect(name) { const full = key(name); if (value(full) === undefined) return undefined; return { key: full, defaultValue: clone(lookup(defaults, full)), globalValue: clone(lookup(global, full)), workspaceValue: clone(lookup(workspace, full)) }; },
      async update(name, newValue, target = 2, overrideInLanguage = false) {
        target = target === true ? 1 : target === false || target == null ? 2 : target;
        if (overrideInLanguage || ![1, 2].includes(target)) throw new Error('Only user and workspace configuration targets are implemented.');
        const full = key(name);
        const result = await request('configuration.update', { key: full, value: newValue, remove: newValue === undefined, target });
        if (result !== true) throw new Error('The workbench rejected the configuration update.');
      }
    };
  }
  return { update, getConfiguration };
}
