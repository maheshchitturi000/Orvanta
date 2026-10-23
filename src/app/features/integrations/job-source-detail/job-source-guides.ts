/** One credential the user copies from the provider's site. */
export interface KeyField {
  id: string;
  label: string;
  placeholder: string;
}

/** How to get and paste the credentials for one job search source. */
export interface JobSourceGuide {
  /** Provider site that issues the key, e.g. "RapidAPI". */
  provider: string;
  signUpUrl: string;
  /** Shown after the sign-up page opened. */
  steps: string[];
  /** Shown before connecting. */
  overview: string[];
  /** Several fields are sent to the API as "first:second". */
  fields: KeyField[];
  /** Shown once connected. */
  connectedNote: string;
}

export const JOB_SOURCE_GUIDES: Record<string, JobSourceGuide> = {
  jsearch: {
    provider: 'JSearch',
    signUpUrl: 'https://rapidapi.com/letscrape-6bRBa3QguO5/api/jsearch',
    steps: [
      'In that tab, sign up or log in to RapidAPI (free).',
      'Click "Subscribe to Test" and choose the Basic plan (free, about 200 searches a month).',
      'Copy the X-RapidAPI-Key value shown in the code example and paste it below.'
    ],
    overview: ['Get a free key on JSearch (Basic plan).', 'Paste it here.'],
    fields: [{ id: 'apiKey', label: 'API key', placeholder: 'Paste your X-RapidAPI-Key' }],
    connectedNote: 'Daily searches now include LinkedIn, Indeed, Glassdoor and Foundit.'
  },
  adzuna: {
    provider: 'Adzuna',
    signUpUrl: 'https://developer.adzuna.com/signup',
    steps: [
      'In that tab, register for a free Adzuna developer account.',
      'Open Dashboard → API Access Details.',
      'Copy the Application ID and the Application Key into the fields below.'
    ],
    overview: ['Register for a free Adzuna developer account.', 'Paste your Application ID and Key here.'],
    fields: [
      { id: 'appId', label: 'Application ID', placeholder: 'e.g. 1a2b3c4d' },
      { id: 'appKey', label: 'Application Key', placeholder: 'Paste your Application Key' }
    ],
    connectedNote: 'Daily searches now include jobs Adzuna gathers from Indian job boards and company sites.'
  }
};
