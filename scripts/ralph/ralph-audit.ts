#!/usr/bin/env bun

import { promises as fs } from 'fs'
import * as path from 'path'
import { Glob } from 'bun'

interface UserStory {
    id: string
    title: string
    passes: boolean
    priority: number
}

interface Prd {
    project: string
    branchName: string
    description: string
    userStories: UserStory[]
}

interface AuditResult {
    branch: string
    description: string
    status: 'completed' | 'in-progress' | 'not-started'
    total: number
    passed: number
    remaining: number
    nextStory: string | null
}

const WORKTREES_DIR = path.resolve(
    import.meta.dir,
    '../../.claude/worktrees/ralph'
)

const COLORS = {
    green: '\x1b[32m',
    yellow: '\x1b[33m',
    red: '\x1b[31m',
    dim: '\x1b[2m',
    bold: '\x1b[1m',
    reset: '\x1b[0m',
    cyan: '\x1b[36m',
}

function statusColor(status: AuditResult['status']): string {
    if (status === 'completed') return COLORS.green
    if (status === 'in-progress') return COLORS.yellow
    return COLORS.red
}

function statusLabel(status: AuditResult['status']): string {
    if (status === 'completed') return 'COMPLETED'
    if (status === 'in-progress') return 'IN PROGRESS'
    return 'NOT STARTED'
}

async function findPrdFiles(): Promise<string[]> {
    const files: string[] = []
    const glob = new Glob('*/scripts/ralph/prd.json')
    for await (const match of glob.scan({
        cwd: WORKTREES_DIR,
        absolute: true,
    })) {
        files.push(match)
    }
    return files.sort()
}

function auditPrd(prd: Prd): AuditResult {
    const total = prd.userStories.length
    const passed = prd.userStories.filter((s) => s.passes).length
    const remaining = total - passed

    let status: AuditResult['status']
    if (passed === total) status = 'completed'
    else if (passed === 0) status = 'not-started'
    else status = 'in-progress'

    const next = prd.userStories
        .filter((s) => !s.passes)
        .sort((a, b) => a.priority - b.priority)[0]

    return {
        branch: prd.branchName,
        description: prd.description,
        status,
        total,
        passed,
        remaining,
        nextStory: next ? `${next.id} — ${next.title}` : null,
    }
}

function printResults(results: AuditResult[]) {
    const completed = results.filter((r) => r.status === 'completed')
    const inProgress = results.filter((r) => r.status === 'in-progress')
    const notStarted = results.filter((r) => r.status === 'not-started')

    console.log(`\n${COLORS.bold}Ralph PRD Audit${COLORS.reset}`)
    console.log(`${COLORS.dim}${'─'.repeat(60)}${COLORS.reset}\n`)

    const sections: { label: string; items: AuditResult[] }[] = [
        { label: 'In Progress', items: inProgress },
        { label: 'Not Started', items: notStarted },
        { label: 'Completed', items: completed },
    ]

    for (const section of sections) {
        if (section.items.length === 0) continue

        const first = section.items[0]
        const color = statusColor(first.status)
        console.log(
            `${color}${COLORS.bold}${section.label} (${section.items.length})${COLORS.reset}\n`
        )

        for (const r of section.items) {
            const bar = `${r.passed}/${r.total}`
            console.log(
                `  ${color}${statusLabel(r.status).padEnd(12)}${COLORS.reset} ${COLORS.cyan}${r.branch}${COLORS.reset}`
            )
            console.log(`  ${COLORS.dim}${r.description}${COLORS.reset}`)
            console.log(`  ${COLORS.dim}Stories: ${bar}${COLORS.reset}`)
            if (r.nextStory) {
                console.log(
                    `  ${COLORS.dim}Next: ${r.nextStory}${COLORS.reset}`
                )
            }
            console.log()
        }
    }

    // Summary
    console.log(`${COLORS.dim}${'─'.repeat(60)}${COLORS.reset}`)
    console.log(
        `${COLORS.bold}Total:${COLORS.reset} ${results.length} PRDs — ` +
            `${COLORS.green}${completed.length} completed${COLORS.reset}, ` +
            `${COLORS.yellow}${inProgress.length} in progress${COLORS.reset}, ` +
            `${COLORS.red}${notStarted.length} not started${COLORS.reset}\n`
    )
}

async function main() {
    const prdFiles = await findPrdFiles()

    if (prdFiles.length === 0) {
        console.log(
            'No prd.json files found in .claude/worktrees/ralph/*/scripts/ralph/'
        )
        process.exit(0)
    }

    const results: AuditResult[] = []

    for (const file of prdFiles) {
        const content = await fs.readFile(file, 'utf-8')
        const prd: Prd = JSON.parse(content)
        results.push(auditPrd(prd))
    }

    // Sort: in-progress first, then not-started, then completed
    results.sort((a, b) => {
        const order = { 'in-progress': 0, 'not-started': 1, completed: 2 }
        return order[a.status] - order[b.status]
    })

    printResults(results)
}

main().catch((err) => {
    console.error('Error:', err.message)
    process.exit(1)
})
