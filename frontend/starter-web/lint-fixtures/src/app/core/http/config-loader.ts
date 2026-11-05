// Lint fixture: core/http may use fetch (the runtime config loads before HttpClient exists).
export async function loadConfig(path: string): Promise<unknown> {
  const response = await fetch(path);
  return response.json();
}
