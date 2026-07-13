import { createPublicSupabaseClient } from "@longevity/supabase";

export const supabase = createPublicSupabaseClient({
  url: process.env.NEXT_PUBLIC_SUPABASE_URL,
  anonKey: process.env.NEXT_PUBLIC_SUPABASE_ANON_KEY,
});
