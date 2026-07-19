import { createClient, type SupabaseClient } from "@supabase/supabase-js";

export interface SupabaseClientConfig {
  url?: string;
  anonKey?: string;
}

export function createPublicSupabaseClient(
  config: SupabaseClientConfig,
): SupabaseClient | null {
  if (!config.url || !config.anonKey) {
    return null;
  }

  return createClient(config.url, config.anonKey);
}
