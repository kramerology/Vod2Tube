import { useCallback, useEffect, useRef, useState } from 'react';
import {
  vodsApi,
  type PipelineJob,
  isActive, isPending, isCompleted, isFailed,
  formatDuration, stageLabel, formatEta,
} from '../api/client';

// ── Status helpers ────────────────────────────────────────────────────────────

function statusDot(job: PipelineJob): string {
  if (job.failed) return 'bg-error';
  if (isActive(job)) return 'bg-primary animate-pulse';
  if (isPending(job)) return 'bg-on-secondary-container/30';
  if (job.stage === 'Uploaded') return 'bg-emerald-500';
  return 'bg-outline';
}

function statusColor(job: PipelineJob): string {
  if (job.failed) return 'text-error';
  if (isActive(job)) return 'text-primary';
  if (job.stage === 'Uploaded') return 'text-emerald-400';
  if (job.stage === 'Cancelled') return 'text-on-surface-variant/50';
  return 'text-on-secondary-container/50';
}

// ── Queue Row ─────────────────────────────────────────────────────────────────

function QueueRow({
  job,
  thumbnailUrl,
  onPause, onResume, onCancel, onRetry,
}: {
  job: PipelineJob;
  thumbnailUrl?: string;
  onPause: () => void;
  onResume: () => void;
  onCancel: () => void;
  onRetry: () => void;
}) {
  const done = isCompleted(job);
  const failed = isFailed(job);
  const active = isActive(job);
  const pending = isPending(job);

  return (
    <div className={`grid grid-cols-12 gap-4 px-6 py-5 hover:bg-white/[0.02] transition-colors items-center ${failed ? 'bg-error-container/5' : ''}`}>
      {/* Media Asset & ID — col-span-5 */}
      <div className="col-span-5 flex gap-4">
        <div className="h-12 w-20 bg-surface-container-highest rounded-lg overflow-hidden flex-shrink-0 relative">
          {thumbnailUrl ? (
            <img
              src={thumbnailUrl}
              alt={job.title}
              className={`w-full h-full object-cover ${failed ? 'grayscale opacity-20' : done ? 'grayscale opacity-50' : 'grayscale opacity-50'}`}
            />
          ) : pending ? (
            <div className="w-full h-full flex items-center justify-center">
              <span className="material-symbols-outlined text-on-surface-variant/30 text-3xl">timer</span>
            </div>
          ) : (
            <div className="w-full h-full flex items-center justify-center">
              <span className="material-symbols-outlined text-on-surface-variant/20 text-2xl">movie</span>
            </div>
          )}
          {(active || thumbnailUrl) && !failed && <div className="absolute inset-0 bg-primary/10" />}
        </div>
        <div className="flex flex-col justify-center min-w-0">
          <span className={`text-sm font-bold truncate ${failed ? 'text-error/90' : done ? 'text-on-surface-variant' : 'text-on-surface'}`}>
            {job.title || job.vodId}
          </span>
          <span className={`text-[10px] font-mono ${failed ? 'text-on-error-container/50' : 'text-on-surface-variant/70'}`}>
            {job.channelName}{job.channelName && ' · '}{formatDuration(job.duration)}
          </span>
        </div>
      </div>

      {/* Status & Stage — col-span-2 */}
      <div className="col-span-2">
        {failed ? (
          <div className="flex flex-col gap-1">
            <div className="flex items-center gap-2">
              <span className={`h-1.5 w-1.5 rounded-full ${statusDot(job)}`} />
              <span className={`text-[11px] font-bold uppercase ${statusColor(job)}`}>{stageLabel(job)}</span>
            </div>
            {job.failReason && (
              <span className="text-[9px] text-error/60 leading-tight line-clamp-1">{job.failReason}</span>
            )}
          </div>
        ) : active ? (
          <div className="flex flex-col gap-2">
            <div className="flex items-center gap-2">
              <span className={`h-1.5 w-1.5 rounded-full ${statusDot(job)}`} />
              <span className={`text-[11px] font-bold uppercase ${statusColor(job)}`}>{stageLabel(job)}</span>
            </div>
            <div className="w-24 bg-surface-container-highest h-1 rounded-full">
              <div
                className="bg-primary h-full rounded-full transition-all duration-500"
                style={{ width: job.percentComplete != null ? `${Math.min(job.percentComplete, 100)}%` : '100%' }}
              />
            </div>
          </div>
        ) : (
          <div className="flex items-center gap-2">
            <span className={`h-1.5 w-1.5 rounded-full ${statusDot(job)}`} />
            <span className={`text-[11px] font-bold uppercase ${statusColor(job)}`}>
              {job.paused ? 'Paused' : stageLabel(job)}
            </span>
          </div>
        )}
      </div>

      {/* Processing Detail — col-span-3 */}
      <div className="col-span-3">
        <div className="flex flex-col">
          <span className={`text-xs ${done || failed ? 'text-on-surface-variant/50' : 'text-on-surface'}`}>
            {job.description || stageLabel(job)}
          </span>
          <span className="text-[10px] text-on-surface-variant">
            {active && job.percentComplete != null
              ? `${job.percentComplete.toFixed(1)}%${job.estimatedMinutesRemaining != null ? ` · ETA ${formatEta(job.estimatedMinutesRemaining)}` : ''}`
              : `Added ${new Date(job.addedAtUTC || job.createdAtUTC).toLocaleDateString('en-US', { month: 'short', day: 'numeric' })}`}
          </span>
        </div>
      </div>

      {/* Actions — col-span-2 */}
      <div className="col-span-2 flex justify-end gap-2">
        {done && (
          <>
            {job.youtubeVideoId && (
              <a href={`https://www.youtube.com/watch?v=${job.youtubeVideoId}`} target="_blank" rel="noreferrer"
                className="p-1.5 text-on-surface-variant hover:text-on-surface hover:bg-white/5 rounded">
                <span className="material-symbols-outlined text-lg">play_circle</span>
              </a>
            )}
            <button onClick={onRetry} className="p-1.5 text-primary hover:bg-primary/10 rounded">
              <span className="material-symbols-outlined text-lg">refresh</span>
            </button>
          </>
        )}
        {failed && (
          <>
            <button onClick={onRetry} className="p-1.5 text-primary hover:bg-primary/10 rounded">
              <span className="material-symbols-outlined text-lg">refresh</span>
            </button>
            <button onClick={onCancel} className="p-1.5 text-on-surface-variant hover:text-error hover:bg-error/5 rounded">
              <span className="material-symbols-outlined text-lg">delete</span>
            </button>
          </>
        )}
        {!done && !failed && (
          <>
            {job.paused ? (
              <button onClick={onResume} className="p-1.5 text-on-surface-variant hover:text-on-surface hover:bg-white/5 rounded">
                <span className="material-symbols-outlined text-lg">play_arrow</span>
              </button>
            ) : (
              <button onClick={onPause} className="p-1.5 text-on-surface-variant hover:text-on-surface hover:bg-white/5 rounded">
                <span className="material-symbols-outlined text-lg">pause</span>
              </button>
            )}
            <button onClick={onCancel} className="p-1.5 text-on-surface-variant hover:text-error hover:bg-error/5 rounded">
              <span className="material-symbols-outlined text-lg">delete</span>
            </button>
          </>
        )}
      </div>
    </div>
  );
}

// ── Filter types ──────────────────────────────────────────────────────────────

type Filter = 'all' | 'active' | 'pending' | 'paused' | 'failed' | 'completed';

// ── Page ──────────────────────────────────────────────────────────────────────

export default function VodsPage() {
  const [jobs, setJobs] = useState<PipelineJob[]>([]);
  const [thumbnailUrls, setThumbnailUrls] = useState<Record<string, string>>({});
  const fetchedIdsRef = useRef<Set<string>>(new Set());
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState<Filter>('all');
  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  const load = useCallback(async () => {
    try {
      setError(null);
      const data = await vodsApi.getAll();
      setJobs(data);

      const newIds = data.map(j => j.vodId).filter(id => !fetchedIdsRef.current.has(id));
      if (newIds.length > 0) {
        newIds.forEach(id => fetchedIdsRef.current.add(id));
        vodsApi.getThumbnailUrls(newIds)
          .then(urls => setThumbnailUrls(prev => ({ ...prev, ...urls })))
          .catch(() => {});
      }
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    load();
    timerRef.current = setInterval(load, 3000);
    return () => { if (timerRef.current) clearInterval(timerRef.current); };
  }, [load]);

  async function act(fn: () => Promise<void>) {
    await fn();
    await load();
  }

  const filtered = jobs.filter(j => {
    if (filter === 'all')       return true;
    if (filter === 'active')    return isActive(j);
    if (filter === 'pending')   return isPending(j);
    if (filter === 'paused')    return j.paused;
    if (filter === 'failed')    return isFailed(j);
    if (filter === 'completed') return isCompleted(j);
    return true;
  });

  const counts = {
    all:       jobs.length,
    active:    jobs.filter(isActive).length,
    pending:   jobs.filter(isPending).length,
    paused:    jobs.filter(j => j.paused).length,
    failed:    jobs.filter(isFailed).length,
    completed: jobs.filter(isCompleted).length,
  };

  const filters: { key: Filter; label: string }[] = [
    { key: 'all',       label: 'All' },
    { key: 'pending',   label: 'Pending' },
    { key: 'active',    label: 'Active' },
    { key: 'paused',    label: 'Paused' },
    { key: 'failed',    label: 'Failed' },
    { key: 'completed', label: 'Completed' },
  ];

  return (
    <>
      {/* Header */}
      <div className="flex flex-col gap-6 mb-10">
        <div className="flex justify-between items-end">
          <div>
            <h1 className="text-3xl font-bold tracking-tight text-on-surface mb-1">Queue Manager</h1>
            <p className="text-on-surface-variant text-sm">Active transcoding jobs and distribution pipeline.</p>
          </div>
        </div>

        {/* Filter Pills */}
        <div className="flex items-center gap-2 overflow-x-auto pb-2">
          {filters.map(f => (
            <button
              key={f.key}
              onClick={() => setFilter(f.key)}
              className={`px-4 py-1.5 rounded-full text-xs font-medium transition-all ${
                filter === f.key
                  ? 'bg-primary text-on-primary-container font-bold'
                  : f.key === 'failed' && counts.failed > 0
                    ? 'bg-surface-container text-error/80 hover:bg-surface-container-high'
                    : 'bg-surface-container text-on-surface-variant hover:bg-surface-container-high'
              } ${f.key === 'active' && counts.active > 0 && filter !== f.key ? 'border border-primary/20' : ''}`}
            >
              {f.label}{counts[f.key] > 0 && ` (${counts[f.key]})`}
            </button>
          ))}
          <div className="h-4 w-px bg-outline-variant/30 mx-2" />
          <button className="text-on-surface-variant text-xs flex items-center gap-1 hover:text-on-surface">
            <span className="material-symbols-outlined text-sm">filter_list</span>
            More Filters
          </button>
        </div>
      </div>

      {/* Error */}
      {error && (
        <div className="mb-6 p-4 bg-error-container/10 border border-white/5 rounded-xl text-error flex items-center gap-3">
          <span className="material-symbols-outlined">error</span>
          <span className="text-sm">{error}</span>
          <button onClick={load} className="ml-auto text-xs font-bold text-primary hover:underline">Retry</button>
        </div>
      )}

      {/* Loading */}
      {loading && (
        <div className="flex items-center justify-center py-20">
          <div className="w-10 h-10 border-2 border-primary/30 border-t-primary rounded-full animate-spin" />
        </div>
      )}

      {/* High-Density Table List */}
      {!loading && filtered.length > 0 && (
        <div className="bg-surface-container-low rounded-xl border border-white/5 overflow-hidden">
          {/* Table Header */}
          <div className="grid grid-cols-12 gap-4 px-6 py-4 border-b border-white/5 bg-surface-container/50 text-[10px] uppercase tracking-widest font-bold text-on-surface-variant">
            <div className="col-span-5">Media Asset &amp; ID</div>
            <div className="col-span-2">Status &amp; Stage</div>
            <div className="col-span-3">Processing Detail</div>
            <div className="col-span-2 text-right">Actions</div>
          </div>

          {/* Queue Rows */}
          <div className="divide-y divide-white/5">
            {filtered.map(job => (
              <QueueRow
                key={job.vodId}
                job={job}
                thumbnailUrl={thumbnailUrls[job.vodId]}
                onPause={() => act(() => vodsApi.pause(job.vodId))}
                onResume={() => act(() => vodsApi.resume(job.vodId))}
                onCancel={() => act(() => vodsApi.cancel(job.vodId))}
                onRetry={() => act(() => vodsApi.retry(job.vodId))}
              />
            ))}
          </div>

          {/* Footer */}
          <div className="px-6 py-4 bg-surface-container-highest/20 flex justify-between items-center text-[10px] text-on-surface-variant border-t border-white/5">
            <div className="flex gap-4">
              <span>Total Items: {jobs.length}</span>
              <span>Active: {counts.active}</span>
              <span>Failed: {counts.failed}</span>
            </div>
            <div className="flex gap-1 items-center">
              <span className="px-2 font-bold text-on-surface">Showing {filtered.length} of {jobs.length}</span>
            </div>
          </div>
        </div>
      )}

      {/* Empty state */}
      {!loading && filtered.length === 0 && (
        <div className="bg-surface-container-low rounded-xl border border-white/5 flex flex-col items-center justify-center py-24 gap-4">
          <span className="material-symbols-outlined text-5xl text-on-surface-variant/30">video_library</span>
          <p className="text-on-surface text-lg font-bold">No VODs match this filter</p>
          <p className="text-on-surface-variant text-sm">
            {filter === 'all' ? 'No jobs in the queue yet.' : 'Try selecting a different filter above.'}
          </p>
        </div>
      )}

      {/* Bento Insight Cards */}
      {!loading && jobs.length > 0 && (
        <div className="grid grid-cols-4 gap-4 mt-8">
          <div className="col-span-1 bg-surface-container border border-white/5 rounded-xl p-4 flex flex-col justify-between">
            <span className="text-[10px] font-bold text-on-surface-variant uppercase">Active Jobs</span>
            <div className="mt-2">
              <span className="text-xl font-bold text-on-surface">{counts.active}</span>
            </div>
          </div>
          <div className="col-span-1 bg-surface-container border border-white/5 rounded-xl p-4 flex flex-col justify-between">
            <span className="text-[10px] font-bold text-on-surface-variant uppercase">Pending</span>
            <div className="mt-2">
              <span className="text-xl font-bold text-on-surface">{counts.pending}</span>
              <p className="text-[10px] text-on-surface-variant mt-1">{counts.paused} paused</p>
            </div>
          </div>
          <div className="col-span-1 bg-surface-container border border-white/5 rounded-xl p-4 flex flex-col justify-between">
            <span className="text-[10px] font-bold text-on-surface-variant uppercase">Completed</span>
            <div className="mt-2">
              <span className="text-xl font-bold text-on-surface">{counts.completed}</span>
            </div>
          </div>
          <div className="col-span-1 bg-surface-container border border-white/5 rounded-xl p-4 flex flex-col justify-between relative overflow-hidden">
            <div className="relative z-10">
              <span className="text-[10px] font-bold text-on-surface-variant uppercase">Pipeline Health</span>
              <div className="flex items-center gap-4 mt-2">
                <span className="text-xl font-bold text-on-surface">
                  {jobs.length > 0 ? `${(((jobs.length - counts.failed) / jobs.length) * 100).toFixed(1)}%` : '—'}
                </span>
                {counts.failed > 0 && (
                  <span className="text-[10px] bg-error/10 text-error px-2 py-0.5 rounded-full font-bold">
                    {counts.failed} failed
                  </span>
                )}
              </div>
            </div>
            <div className="absolute right-0 bottom-0 opacity-10">
              <span className="material-symbols-outlined text-8xl rotate-12">monitoring</span>
            </div>
          </div>
        </div>
      )}
    </>
  );
}
