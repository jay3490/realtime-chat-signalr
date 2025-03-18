/** Same rule as the server's Mentions.Find: `@name`, not preceded by a name character. */
const MENTION = /(?<![A-Za-z0-9._@-])@([A-Za-z0-9][A-Za-z0-9._-]{1,31})/g;

export interface MessageSegment {
  text: string;
  /** Lower-cased user name when this segment is a mention. */
  mention?: string;
}

/** Splits a message body into plain text and `@mention` segments for highlighting. */
export function splitMentions(body: string): MessageSegment[] {
  const segments: MessageSegment[] = [];
  let last = 0;
  for (const match of body.matchAll(MENTION)) {
    // A trailing full stop ends the sentence, not the name: "thanks @bob."
    const raw = match[0].replace(/\.+$/, '');
    const name = match[1].replace(/\.+$/, '');
    if (name.length < 2) {
      continue;
    }
    const start = match.index ?? 0;
    if (start > last) {
      segments.push({ text: body.slice(last, start) });
    }
    segments.push({ text: raw, mention: name.toLowerCase() });
    last = start + raw.length;
  }
  if (last < body.length) {
    segments.push({ text: body.slice(last) });
  }
  return segments;
}

export function mentions(body: string, userName: string | null): boolean {
  return !!userName && splitMentions(body).some((s) => s.mention === userName);
}
